using System.Text.Json;
using Microsoft.AspNetCore.Http;
using Serilog;
using Serilog.Core;
using Serilog.Events;
using LogTier.Serilog;

namespace LogTier.Serilog.IntegrationTests;

/// <summary>End-to-end contract: Serilog events, exceptions, request enrichment and captured Trace/Console/EventSource output all come out as M-21-31 EL1 JSON.</summary>
public sealed class LogTierSerilogTests
{
    [Fact]
    public async Task Events_and_captured_output_follow_the_EL1_contract()
    {
        var options = new LogTierOptions
        {
            ApplicationName = "LogTier.Serilog.IntegrationTests",
            ApplicationVersion = "1.0.0",
            EnvironmentName = "Test",
            DeviceIdentifier = "device-test-001"
        };

        var sink = new CaptureSink();
        using var logger = new LoggerConfiguration()
            .Enrich.FromLogContext()
            .Enrich.WithLogTier(options)
            .WriteTo.Sink(sink)
            .CreateLogger();

        // Captured Trace, Console and EventSource output is written through the static logger.
        Log.Logger = logger;

        logger
            .ForContext(LogTierProperties.TransactionId, "txn-123")
            .ForContext(LogTierProperties.SourceIpv4, "192.0.2.10")
            .ForContext(LogTierProperties.DestinationIpv4, "198.51.100.20")
            .ForContext(LogTierProperties.EventStatusCode, "Success")
            .Information("Smoke test {Value}", 42);

        if (sink.Events.Count != 1)
        {
            throw new InvalidOperationException($"Expected one captured event; got {sink.Events.Count}.");
        }

        using var writer = new StringWriter();
        new LogTierJsonFormatter(options).Format(sink.Events[0], writer);
        var json = writer.ToString();

        using var document = JsonDocument.Parse(json);
        var root = document.RootElement;

        Require(root.GetProperty("m2131").GetProperty("memorandum").GetString() == "M-21-31", "memorandum");
        Require(root.GetProperty("m2131").GetProperty("event_logging_tier").GetString() == "EL1", "EL1 tier");
        Require(root.GetProperty("timestamp").GetString()!.EndsWith('Z'), "UTC timestamp");
        Require(root.GetProperty("timestamp").GetString()!.Length == 24, "millisecond timestamp precision");
        Require(root.GetProperty("event").GetProperty("id").GetString() is { Length: > 0 }, "event id");
        Require(root.GetProperty("event").GetProperty("type_id").GetString() is { Length: > 0 }, "event type id");
        Require(root.GetProperty("device").GetProperty("id").GetString() == "device-test-001", "device id");
        Require(root.GetProperty("session").GetProperty("transaction_id").GetString() == "txn-123", "transaction id");
        Require(root.GetProperty("network").GetProperty("source").GetProperty("ipv4").GetString() == "192.0.2.10", "source IPv4");
        Require(root.GetProperty("network").GetProperty("destination").GetProperty("ipv4").GetString() == "198.51.100.20", "destination IPv4");
        Require(root.GetProperty("properties").GetProperty("Value").GetInt32() == 42, "preserved Serilog property");

        // Exceptions keep their inner chain, and the automatic type ID is the same for every event from one template.
        sink.Events.Clear();
        logger.Error(new InvalidOperationException("outer", new ArgumentException("inner")), "Failed {Step}", 1);
        logger.Error("Failed {Step}", 2);
        using (var failure = JsonDocument.Parse(Format(options, sink.Events[0])))
        using (var other = JsonDocument.Parse(Format(options, sink.Events[1])))
        {
            var exception = failure.RootElement.GetProperty("exception");
            Require(exception.GetProperty("inner").GetProperty("message").GetString() == "inner", "inner exception");
            Require(failure.RootElement.GetProperty("event").GetProperty("type_id").GetString()
                    == other.RootElement.GetProperty("event").GetProperty("type_id").GetString(), "stable automatic event type ID");
        }

        // Request middleware: allow-listed headers, redaction, X-Request-ID as the transaction ID, network data switched off.
        options.IncludeNetworkEndpointData = false;
        options.HttpHeaderAllowList.Add("Authorization");
        sink.Events.Clear();
        var context = new DefaultHttpContext();
        context.Request.Method = "GET";
        context.Request.Scheme = "https";
        context.Request.Host = new HostString("api.example.test");
        context.Request.Path = "/cases/42";
        context.Request.Headers["X-Request-ID"] = "req-777";
        context.Request.Headers["User-Agent"] = "smoke";
        context.Request.Headers["Authorization"] = "Bearer secret";
        context.Connection.RemoteIpAddress = System.Net.IPAddress.Parse("192.0.2.10");
        var middleware = new LogTierRequestLoggingMiddleware(
            _ =>
            {
                logger.Information("Inside request");
                return Task.CompletedTask;
            },
            options,
            logger);
        await middleware.InvokeAsync(context);
        Require(sink.Events.Count == 2, "request event and completion event");
        foreach (var logEvent in sink.Events)
        {
            using var requestDocument = JsonDocument.Parse(Format(options, logEvent));
            var request = requestDocument.RootElement;
            Require(request.GetProperty("session").GetProperty("transaction_id").GetString() == "req-777", "X-Request-ID transaction id");
            Require(request.GetProperty("http").GetProperty("path").GetString() == "/cases/42", "request path");
            Require(request.GetProperty("http").GetProperty("headers").GetProperty("User-Agent").GetString() == "smoke", "allow-listed header");
            Require(request.GetProperty("http").GetProperty("headers").GetProperty("Authorization").GetString() == "[REDACTED]", "redacted header");
            Require(request.GetProperty("network").GetProperty("source").GetProperty("ipv4").ValueKind == JsonValueKind.Null, "network data switched off");
        }

        using (var completion = JsonDocument.Parse(Format(options, sink.Events[1])))
        {
            Require(completion.RootElement.GetProperty("event").GetProperty("type_id").GetString() == LogTierEventTypes.HttpRequestCompleted, "completion event type");
            Require(completion.RootElement.GetProperty("http").GetProperty("status_code").GetInt32() == 200, "completion status code");
        }

        // Output other libraries write outside Serilog comes back as LogTier events.
        sink.Events.Clear();
        Require(LogTierCapture.IsStarted, "capture on by default");
        System.Diagnostics.Trace.TraceWarning("trace {0}", "warning");
        Console.WriteLine("console line");
        Console.Error.WriteLine("error line");
        SmokeEventSource.Log.Warn("event source");
        LogTierCapture.Stop();
        Log.Logger = global::Serilog.Core.Logger.None;
        RequireCaptured("Trace", "trace warning", LogEventLevel.Warning);
        RequireCaptured("Console", "console line", LogEventLevel.Information);
        RequireCaptured("Console.Error", "error line", LogEventLevel.Warning);
        RequireCaptured("EventSource.LogTier-Smoke", "Smoke event source", LogEventLevel.Warning);

        void RequireCaptured(string sourceContext, string message, LogEventLevel level)
        {
            var captured = sink.Events.FirstOrDefault(e => e.Properties.TryGetValue("SourceContext", out var value)
                && value is ScalarValue { Value: string text } && text.StartsWith(sourceContext, StringComparison.Ordinal)
                && e.RenderMessage(System.Globalization.CultureInfo.InvariantCulture) == message);
            Require(captured is not null && captured.Level == level, $"captured {sourceContext} output");
            using var capturedJson = JsonDocument.Parse(Format(options, captured!));
            Require(capturedJson.RootElement.GetProperty("m2131").GetProperty("event_logging_tier").GetString() == "EL1", $"{sourceContext} output in LogTier format");
        }

        Console.WriteLine("LogTier.Serilog smoke test passed.");
        Console.WriteLine(json);

        static string Format(LogTierOptions formatterOptions, LogEvent logEvent)
        {
            using var output = new StringWriter();
            new LogTierJsonFormatter(formatterOptions).Format(logEvent, output);
            return output.ToString();
        }

        static void Require(bool condition, string description)
        {
            if (!condition)
            {
                throw new InvalidOperationException($"Smoke test failed: {description}.");
            }
        }
    }
}

[System.Diagnostics.Tracing.EventSource(Name = "LogTier-Smoke")]
file sealed class SmokeEventSource : System.Diagnostics.Tracing.EventSource
{
    public static readonly SmokeEventSource Log = new();

    [System.Diagnostics.Tracing.Event(1, Level = System.Diagnostics.Tracing.EventLevel.Warning, Message = "Smoke {0}")]
    public void Warn(string value) => WriteEvent(1, value);
}

file sealed class CaptureSink : ILogEventSink
{
    public List<LogEvent> Events { get; } = [];

    public void Emit(LogEvent logEvent)
        => Events.Add(logEvent);
}
