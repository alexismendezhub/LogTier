using System.Diagnostics;
using System.Net;
using System.Net.Sockets;
using System.Security.Claims;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Primitives;
using Serilog;
using Serilog.Context;
using Serilog.Core;
using Serilog.Events;

namespace LogTier.Serilog;

/// <summary>
/// Enriches request-scoped events and emits a standardized request completion event.
/// </summary>
public sealed class LogTierRequestLoggingMiddleware
{
    private readonly RequestDelegate _next;
    private readonly LogTierOptions _options;
    private readonly global::Serilog.ILogger _logger;

    /// <summary>Creates the middleware with the given options.</summary>
    public LogTierRequestLoggingMiddleware(
        RequestDelegate next,
        LogTierOptions options,
        global::Serilog.ILogger logger)
    {
        _next = next ?? throw new ArgumentNullException(nameof(next));
        _options = options ?? throw new ArgumentNullException(nameof(options));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    /// <summary>Runs the rest of the pipeline inside the request logging scope.</summary>
    public async Task InvokeAsync(HttpContext context)
    {
        ArgumentNullException.ThrowIfNull(context);

        var startTimestamp = Stopwatch.GetTimestamp();
        var path = context.Request.Path.Value ?? "/";

        // One enricher for the whole request instead of one LogContext push per field.
        using var requestScope = LogContext.Push(CreateRequestEnricher(context, path));

        Exception? unhandledException = null;
        try
        {
            await _next(context).ConfigureAwait(false);
        }
        catch (Exception exception)
        {
            unhandledException = exception;
            throw;
        }
        finally
        {
            if (_options.EmitHttpCompletionEvent)
            {
                var elapsedTicks = Stopwatch.GetTimestamp() - startTimestamp;
                var responseTimeMs = Math.Round(elapsedTicks * 1000d / Stopwatch.Frequency, 3);
                WriteCompletionEvent(context, path, responseTimeMs, unhandledException);
            }
        }
    }

    private void WriteCompletionEvent(HttpContext context, string path, double responseTimeMs, Exception? unhandledException)
    {
        var effectiveStatusCode = unhandledException is null
            ? context.Response.StatusCode
            : StatusCodes.Status500InternalServerError;

        var requestLogger = _logger
            .ForContext(LogTierProperties.EventTypeId, LogTierEventTypes.HttpRequestCompleted)
            .ForContext(LogTierProperties.EventCategory, LogTierEventCategories.ApplicationTransaction)
            .ForContext(LogTierProperties.EventStatusCode, unhandledException is null ? "Completed" : "Failed")
            .ForContext(LogTierProperties.HttpStatusCode, effectiveStatusCode)
            .ForContext(LogTierProperties.ResponseTimeMs, responseTimeMs);

        if (unhandledException is null)
        {
            requestLogger.Information(
                "HTTP {HttpMethod} {HttpPath} completed with status {HttpStatusCode} in {ResponseTimeMs} ms",
                context.Request.Method,
                path,
                effectiveStatusCode,
                responseTimeMs);
        }
        else
        {
            requestLogger.Error(
                unhandledException,
                "HTTP {HttpMethod} {HttpPath} failed with status {HttpStatusCode} in {ResponseTimeMs} ms",
                context.Request.Method,
                path,
                effectiveStatusCode,
                responseTimeMs);
        }
    }

    private RequestEnricher CreateRequestEnricher(HttpContext context, string path)
    {
        var request = context.Request;
        var properties = new List<LogEventProperty>(18)
        {
            Create(LogTierProperties.TransactionId, ResolveTransactionId(context)),
            Create(LogTierProperties.HttpMethod, request.Method),
            Create(LogTierProperties.HttpScheme, request.Scheme),
            Create(LogTierProperties.HttpHost, request.Host.Value),
            Create(LogTierProperties.HttpPath, path),
            Create(LogTierProperties.Command, $"{request.Method} {path}")
        };

        AddIfPresent(properties, LogTierProperties.SessionId, ResolveSessionId(context));
        AddIfPresent(properties, LogTierProperties.UserId, context.User.FindFirstValue(ClaimTypes.NameIdentifier) ?? context.User.FindFirstValue("sub"));
        AddIfPresent(properties, LogTierProperties.UserName, context.User.Identity?.Name);

        if (_options.IncludeHttpHeaders)
        {
            var headers = CaptureHeaders(request.Headers);
            if (headers is not null)
            {
                properties.Add(new LogEventProperty(LogTierProperties.HttpHeaders, headers));
            }
        }

        if (_options.IncludeNetworkEndpointData)
        {
            var connection = context.Connection;
            var source = SplitIp(connection.RemoteIpAddress);
            var destination = SplitIp(connection.LocalIpAddress);
            AddIfPresent(properties, LogTierProperties.SourceIpv4, source.Ipv4);
            AddIfPresent(properties, LogTierProperties.SourceIpv6, source.Ipv6);
            if (connection.RemoteIpAddress is not null)
            {
                properties.Add(Create(LogTierProperties.SourcePort, connection.RemotePort));
            }

            AddIfPresent(properties, LogTierProperties.DestinationIpv4, destination.Ipv4);
            AddIfPresent(properties, LogTierProperties.DestinationIpv6, destination.Ipv6);
            if (connection.LocalIpAddress is not null)
            {
                properties.Add(Create(LogTierProperties.DestinationPort, connection.LocalPort));
            }
        }

        return new RequestEnricher(properties.ToArray());
    }

    private DictionaryValue? CaptureHeaders(IHeaderDictionary headers)
    {
        List<KeyValuePair<ScalarValue, LogEventPropertyValue>>? captured = null;
        foreach (var headerName in _options.HttpHeaderAllowList)
        {
            if (!headers.TryGetValue(headerName, out var value))
            {
                continue;
            }

            var text = _options.RedactedHttpHeaders.Contains(headerName) ? "[REDACTED]" : value.ToString();
            (captured ??= []).Add(new(new ScalarValue(headerName), new ScalarValue(text)));
        }

        return captured is null ? null : new DictionaryValue(captured);
    }

    private static string ResolveTransactionId(HttpContext context)
    {
        var headers = context.Request.Headers;
        return FirstNonEmpty(headers["X-Correlation-ID"])
               ?? FirstNonEmpty(headers["X-Request-ID"])
               ?? Activity.Current?.TraceId.ToHexString()
               ?? context.TraceIdentifier;
    }

    private static string? ResolveSessionId(HttpContext context)
        => context.User.FindFirstValue("sid") ?? FirstNonEmpty(context.Request.Headers["X-Session-ID"]);

    private static string? FirstNonEmpty(StringValues values)
    {
        var value = values.Count > 0 ? values[0] : null;
        return string.IsNullOrWhiteSpace(value) ? null : value;
    }

    private static (string? Ipv4, string? Ipv6) SplitIp(IPAddress? address)
    {
        if (address is null)
        {
            return (null, null);
        }

        if (address.IsIPv4MappedToIPv6)
        {
            address = address.MapToIPv4();
        }

        return address.AddressFamily switch
        {
            AddressFamily.InterNetwork => (address.ToString(), null),
            AddressFamily.InterNetworkV6 => (null, address.ToString()),
            _ => (null, null)
        };
    }

    private static void AddIfPresent(List<LogEventProperty> properties, string name, string? value)
    {
        if (!string.IsNullOrWhiteSpace(value))
        {
            properties.Add(Create(name, value));
        }
    }

    private static LogEventProperty Create(string name, object? value)
        => new(name, new ScalarValue(value));

    private sealed class RequestEnricher(LogEventProperty[] properties) : ILogEventEnricher
    {
        public void Enrich(LogEvent logEvent, ILogEventPropertyFactory propertyFactory)
        {
            foreach (var property in properties)
            {
                logEvent.AddPropertyIfAbsent(property);
            }
        }
    }
}

/// <summary>ASP.NET Core pipeline helpers for LogTier request logging.</summary>
public static class LogTierApplicationBuilderExtensions
{
    /// <summary>
    /// Adds request enrichment and request completion logging for ASP.NET Core.
    /// Place this after forwarded-header handling and authentication if you want resolved client IP and user identity.
    /// </summary>
    public static IApplicationBuilder UseLogTierRequestLogging(
        this IApplicationBuilder app,
        LogTierOptions options)
    {
        ArgumentNullException.ThrowIfNull(app);
        ArgumentNullException.ThrowIfNull(options);
        return app.UseMiddleware<LogTierRequestLoggingMiddleware>(options, Log.Logger);
    }
}
