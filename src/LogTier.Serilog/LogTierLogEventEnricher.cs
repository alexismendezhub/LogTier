using System.Collections.Concurrent;
using System.Diagnostics;
using System.Net;
using System.Net.Sockets;
using System.Security.Cryptography;
using System.Text;
using Serilog.Core;
using Serilog.Events;

namespace LogTier.Serilog;

/// <summary>
/// Adds stable M-21-31 metadata to every Serilog event.
/// </summary>
public sealed class LogTierLogEventEnricher : ILogEventEnricher
{
    // Bounds the event type ID cache so templates built by string concatenation cannot grow it without limit.
    private const int MaxCachedEventTypeIds = 10_000;

    private readonly LogEventProperty[] _staticProperties;
    private readonly ConcurrentDictionary<(string SourceContext, string Template), LogEventProperty> _eventTypeIds = new();

    /// <summary>Creates the enricher with the given options.</summary>
    public LogTierLogEventEnricher(LogTierOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);

        var hostName = GetHostName();
        var deviceId = string.IsNullOrWhiteSpace(options.DeviceIdentifier) ? hostName : options.DeviceIdentifier.Trim();

        // Values that never change for the life of the process are created once instead of on every event.
        var properties = new List<LogEventProperty>
        {
            Create(LogTierProperties.Memorandum, LogTierStandard.Memorandum),
            Create(LogTierProperties.EventLoggingTier, LogTierStandard.EventLoggingTier),
            Create(LogTierProperties.SchemaVersion, options.SchemaVersion),
            Create(LogTierProperties.DeviceId, deviceId),
            Create(LogTierProperties.ApplicationName, options.ApplicationName),
            Create(LogTierProperties.EnvironmentName, options.EnvironmentName),
            Create(LogTierProperties.HostName, hostName),
            Create(LogTierProperties.ProcessId, Environment.ProcessId)
        };
        if (!string.IsNullOrWhiteSpace(options.DeviceMacAddress))
        {
            properties.Add(Create(LogTierProperties.DeviceMac, options.DeviceMacAddress));
        }

        if (options.AutonomousSystemNumber is { } asn)
        {
            properties.Add(Create(LogTierProperties.AutonomousSystemNumber, asn));
        }

        if (!string.IsNullOrWhiteSpace(options.ApplicationVersion))
        {
            properties.Add(Create(LogTierProperties.ApplicationVersion, options.ApplicationVersion));
        }

        _staticProperties = properties.ToArray();

        // Every LogTier pipeline also captures what other libraries write outside Serilog.
        LogTierCapture.Start(options);
    }

    /// <inheritdoc />
    public void Enrich(LogEvent logEvent, ILogEventPropertyFactory propertyFactory)
    {
        ArgumentNullException.ThrowIfNull(logEvent);

        foreach (var property in _staticProperties)
        {
            logEvent.AddPropertyIfAbsent(property);
        }

        logEvent.AddPropertyIfAbsent(Create(LogTierProperties.EventId, Guid.NewGuid().ToString("D")));
        logEvent.AddPropertyIfAbsent(Create(LogTierProperties.ThreadId, Environment.CurrentManagedThreadId));

        if (!logEvent.Properties.ContainsKey(LogTierProperties.EventTypeId))
        {
            logEvent.AddPropertyIfAbsent(GetEventTypeId(logEvent));
        }

        var activity = Activity.Current;
        if (activity is not null && activity.IdFormat == ActivityIdFormat.W3C)
        {
            logEvent.AddPropertyIfAbsent(Create(LogTierProperties.TraceId, activity.TraceId.ToHexString()));
            logEvent.AddPropertyIfAbsent(Create(LogTierProperties.SpanId, activity.SpanId.ToHexString()));
        }
    }

    private LogEventProperty GetEventTypeId(LogEvent logEvent)
    {
        var key = (TryGetScalarString(logEvent, "SourceContext") ?? string.Empty, logEvent.MessageTemplate.Text);
        if (_eventTypeIds.TryGetValue(key, out var cached))
        {
            return cached;
        }

        var property = Create(LogTierProperties.EventTypeId, CreateStableEventTypeId(key.Item1, key.Item2));
        if (_eventTypeIds.Count < MaxCachedEventTypeIds)
        {
            _eventTypeIds.TryAdd(key, property);
        }

        return property;
    }

    private static string CreateStableEventTypeId(string sourceContext, string template)
    {
        var digest = SHA256.HashData(Encoding.UTF8.GetBytes($"{sourceContext}|{template}"));
        return $"AUTO-{Convert.ToHexString(digest.AsSpan(0, 8))}";
    }

    private static string? TryGetScalarString(LogEvent logEvent, string propertyName)
        => logEvent.Properties.TryGetValue(propertyName, out var value) && value is ScalarValue scalar
            ? scalar.Value?.ToString()
            : null;

    private static LogEventProperty Create(string name, object? value)
        => new(name, new ScalarValue(value));

    private static string GetHostName()
    {
        try
        {
            return Dns.GetHostName();
        }
        catch (SocketException)
        {
            return Environment.MachineName;
        }
    }
}
