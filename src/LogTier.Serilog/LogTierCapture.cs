using System.Collections.ObjectModel;
using System.Diagnostics;
using System.Diagnostics.Tracing;
using System.Globalization;
using System.Text;
using Serilog;
using Serilog.Events;

namespace LogTier.Serilog;

/// <summary>
/// Captures what other libraries write outside Microsoft.Extensions.Logging and Serilog
/// (System.Diagnostics.Trace, EventSource, Console) and rewrites it through <see cref="Log.Logger"/>
/// so it comes out in the LogTier JSON format. Started automatically by <c>Enrich.WithLogTier</c>.
/// </summary>
public static class LogTierCapture
{
    private static readonly object Gate = new();
    private static LogTierTraceListener? _traceListener;
    private static LogTierEventSourceListener? _eventSourceListener;
    private static TextWriter? _originalOut;
    private static TextWriter? _originalError;

    // Writes done while a captured message is being logged go straight to the original output,
    // so a sink that writes to the console or to Trace can never feed itself.
    [ThreadStatic]
    private static bool _forwarding;

    /// <summary>True while capture is running.</summary>
    public static bool IsStarted { get; private set; }

    internal static bool IsForwarding => _forwarding;

    /// <summary>Starts the captures switched on in <paramref name="options"/>. Only the first call has an effect.</summary>
    public static void Start(LogTierOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);
        lock (Gate)
        {
            if (IsStarted)
            {
                return;
            }

            IsStarted = true;

            if (options.CaptureTrace)
            {
                _traceListener = new LogTierTraceListener();
                Trace.Listeners.Add(_traceListener);
            }

            if (options.CaptureConsole)
            {
                _originalOut = Console.Out;
                _originalError = Console.Error;
                Console.SetOut(new LogTierConsoleWriter(_originalOut, "Console", LogEventLevel.Information));
                Console.SetError(new LogTierConsoleWriter(_originalError, "Console.Error", LogEventLevel.Warning));
            }

            if (options.CaptureEventSources)
            {
                _eventSourceListener = new LogTierEventSourceListener(options.EventSourceMinimumLevel, options.ExcludedEventSources);
            }
        }
    }

    /// <summary>Stops every capture and restores the console.</summary>
    public static void Stop()
    {
        lock (Gate)
        {
            if (!IsStarted)
            {
                return;
            }

            IsStarted = false;

            if (_traceListener is not null)
            {
                Trace.Listeners.Remove(_traceListener);
                _traceListener.Dispose();
                _traceListener = null;
            }

            if (_originalOut is not null && _originalError is not null)
            {
                var capturedOut = Console.Out;
                var capturedError = Console.Error;
                Console.SetOut(_originalOut);
                Console.SetError(_originalError);
                capturedOut.Flush();
                capturedError.Flush();
                _originalOut = null;
                _originalError = null;
            }

            _eventSourceListener?.Dispose();
            _eventSourceListener = null;
        }
    }

    internal static void Forward(string sourceContext, LogEventLevel level, string? message, string captureSource, IReadOnlyDictionary<string, object?>? properties = null)
    {
        if (_forwarding || string.IsNullOrWhiteSpace(message))
        {
            return;
        }

        _forwarding = true;
        try
        {
            var logger = Log.Logger.ForContext("SourceContext", sourceContext).ForContext("CaptureSource", captureSource);
            if (properties is not null)
            {
                foreach (var property in properties)
                {
                    logger = logger.ForContext(property.Key, property.Value, destructureObjects: true);
                }
            }

            // The text is passed as a property so braces in captured output are never read as a message template.
            logger.Write(level, "{CapturedMessage:l}", message);
        }
        finally
        {
            _forwarding = false;
        }
    }
}

/// <summary>Trace listener that sends System.Diagnostics.Trace and TraceSource output to Serilog.</summary>
public sealed class LogTierTraceListener : TraceListener
{
    private readonly StringBuilder _pending = new();

    /// <summary>Creates the listener.</summary>
    public LogTierTraceListener() : base("LogTier")
    {
    }

    /// <inheritdoc />
    public override void Write(string? message)
    {
        if (LogTierCapture.IsForwarding)
        {
            return;
        }

        lock (_pending)
        {
            _pending.Append(message);
        }
    }

    /// <inheritdoc />
    public override void WriteLine(string? message)
    {
        if (LogTierCapture.IsForwarding)
        {
            return;
        }

        string line;
        lock (_pending)
        {
            _pending.Append(message);
            line = _pending.ToString();
            _pending.Clear();
        }

        LogTierCapture.Forward("Trace", LogEventLevel.Debug, line, "Trace");
    }

    /// <inheritdoc />
    public override void TraceEvent(TraceEventCache? eventCache, string source, TraceEventType eventType, int id, string? message)
    {
        if (LogTierCapture.IsForwarding || (Filter is not null && !Filter.ShouldTrace(eventCache, source, eventType, id, message, null, null, null)))
        {
            return;
        }

        var properties = new Dictionary<string, object?> { ["TraceSource"] = source, ["TraceEventId"] = id };
        LogTierCapture.Forward(string.IsNullOrEmpty(source) ? "Trace" : $"Trace.{source}", Map(eventType), message, "Trace", properties);
    }

    /// <inheritdoc />
    public override void TraceEvent(TraceEventCache? eventCache, string source, TraceEventType eventType, int id, string? format, params object?[]? args)
    {
        var message = args is null || args.Length == 0 || format is null
            ? format
            : string.Format(CultureInfo.InvariantCulture, format, args);
        TraceEvent(eventCache, source, eventType, id, message);
    }

    /// <inheritdoc />
    public override void TraceData(TraceEventCache? eventCache, string source, TraceEventType eventType, int id, object? data)
        => TraceEvent(eventCache, source, eventType, id, Convert.ToString(data, CultureInfo.InvariantCulture));

    /// <inheritdoc />
    public override void TraceData(TraceEventCache? eventCache, string source, TraceEventType eventType, int id, params object?[]? data)
        => TraceEvent(eventCache, source, eventType, id, data is null ? null : string.Join(", ", data.Select(d => Convert.ToString(d, CultureInfo.InvariantCulture))));

    /// <inheritdoc />
    public override void Fail(string? message, string? detailMessage)
        => LogTierCapture.Forward("Trace", LogEventLevel.Error, string.IsNullOrEmpty(detailMessage) ? message : $"{message} {detailMessage}", "Trace");

    private static LogEventLevel Map(TraceEventType eventType) => eventType switch
    {
        TraceEventType.Critical => LogEventLevel.Fatal,
        TraceEventType.Error => LogEventLevel.Error,
        TraceEventType.Warning => LogEventLevel.Warning,
        TraceEventType.Information => LogEventLevel.Information,
        _ => LogEventLevel.Debug
    };
}

/// <summary>Listens to EventSource events (Azure SDK, System.Net, Kestrel and others) and sends them to Serilog.</summary>
public sealed class LogTierEventSourceListener : EventListener
{
    // Sources created before the constructor body runs are queued here; field initializers run before the base constructor.
    private readonly List<EventSource> _pendingSources = [];
    private readonly EventLevel _level;
    private readonly HashSet<string> _excluded;
    private readonly bool _ready;

    /// <summary>Creates the listener for every EventSource except <paramref name="excludedSources"/>.</summary>
    public LogTierEventSourceListener(EventLevel level, IEnumerable<string> excludedSources)
    {
        _level = level;
        _excluded = new HashSet<string>(excludedSources, StringComparer.OrdinalIgnoreCase);
        List<EventSource> pending;
        lock (_pendingSources)
        {
            _ready = true;
            pending = [.. _pendingSources];
            _pendingSources.Clear();
        }

        foreach (var source in pending)
        {
            Enable(source);
        }
    }

    /// <inheritdoc />
    protected override void OnEventSourceCreated(EventSource eventSource)
    {
        if (_pendingSources is null)
        {
            return;
        }

        lock (_pendingSources)
        {
            if (!_ready)
            {
                _pendingSources.Add(eventSource);
                return;
            }
        }

        Enable(eventSource);
    }

    private void Enable(EventSource eventSource)
    {
        if (!_excluded.Contains(eventSource.Name))
        {
            EnableEvents(eventSource, _level, EventKeywords.All);
        }
    }

    /// <inheritdoc />
    protected override void OnEventWritten(EventWrittenEventArgs eventData)
    {
        if (LogTierCapture.IsForwarding || !_ready || _excluded.Contains(eventData.EventSource.Name))
        {
            return;
        }

        var payload = new Dictionary<string, object?>(StringComparer.Ordinal);
        if (eventData.PayloadNames is not null && eventData.Payload is not null)
        {
            for (var i = 0; i < eventData.PayloadNames.Count && i < eventData.Payload.Count; i++)
            {
                payload[eventData.PayloadNames[i]] = eventData.Payload[i];
            }
        }

        var properties = new Dictionary<string, object?>
        {
            ["EventSourceName"] = eventData.EventSource.Name,
            ["EventSourceEventName"] = eventData.EventName,
            ["EventSourceEventId"] = eventData.EventId,
            ["EventSourcePayload"] = payload
        };
        LogTierCapture.Forward($"EventSource.{eventData.EventSource.Name}", Map(eventData.Level), FormatMessage(eventData), "EventSource", properties);
    }

    private static string? FormatMessage(EventWrittenEventArgs eventData)
    {
        var payload = eventData.Payload ?? new ReadOnlyCollection<object?>([]);
        if (!string.IsNullOrEmpty(eventData.Message))
        {
            try
            {
                return string.Format(CultureInfo.InvariantCulture, eventData.Message, [.. payload]);
            }
            catch (FormatException)
            {
                return eventData.Message;
            }
        }

        var names = eventData.PayloadNames;
        if (names is null || names.Count == 0)
        {
            return eventData.EventName;
        }

        return $"{eventData.EventName} " + string.Join(", ", names.Select((n, i) => $"{n}={Convert.ToString(i < payload.Count ? payload[i] : null, CultureInfo.InvariantCulture)}"));
    }

    private static LogEventLevel Map(EventLevel level) => level switch
    {
        EventLevel.Critical => LogEventLevel.Fatal,
        EventLevel.Error => LogEventLevel.Error,
        EventLevel.Warning => LogEventLevel.Warning,
        EventLevel.Informational or EventLevel.LogAlways => LogEventLevel.Information,
        _ => LogEventLevel.Debug
    };
}

/// <summary>Console writer that turns each line other code writes into a Serilog event.</summary>
internal sealed class LogTierConsoleWriter(TextWriter original, string sourceContext, LogEventLevel level) : TextWriter
{
    // Lines that already are LogTier JSON (written by a console sink using the LogTier formatter) pass straight through.
    private const string LogTierJsonPrefix = "{\"timestamp\":";

    private readonly StringBuilder _line = new();

    public override Encoding Encoding => original.Encoding;

    public override void Write(char value)
    {
        if (LogTierCapture.IsForwarding)
        {
            original.Write(value);
            return;
        }

        string? completed = null;
        lock (_line)
        {
            if (value == '\n')
            {
                completed = _line.ToString().TrimEnd('\r');
                _line.Clear();
            }
            else
            {
                _line.Append(value);
            }
        }

        if (completed is not null)
        {
            Emit(completed);
        }
    }

    public override void Write(string? value)
    {
        if (value is null)
        {
            return;
        }

        if (LogTierCapture.IsForwarding || IsWholeLogTierLine(value))
        {
            original.Write(value);
            return;
        }

        foreach (var c in value)
        {
            Write(c);
        }
    }

    public override void Write(char[] buffer, int index, int count)
    {
        if (LogTierCapture.IsForwarding)
        {
            original.Write(buffer, index, count);
            return;
        }

        for (var i = index; i < index + count; i++)
        {
            Write(buffer[i]);
        }
    }

    public override void Flush()
    {
        string? partial;
        lock (_line)
        {
            partial = _line.Length > 0 ? _line.ToString() : null;
            _line.Clear();
        }

        if (partial is not null)
        {
            Emit(partial);
        }

        original.Flush();
    }

    // Fast path for a complete LogTier JSON line written in one call, the way the LogTier console sinks write.
    private bool IsWholeLogTierLine(string value)
    {
        if (!value.StartsWith(LogTierJsonPrefix, StringComparison.Ordinal) || value.IndexOf('\n') != value.Length - 1)
        {
            return false;
        }

        lock (_line)
        {
            return _line.Length == 0;
        }
    }

    private void Emit(string line)
    {
        if (line.Length == 0)
        {
            return;
        }

        if (line.StartsWith(LogTierJsonPrefix, StringComparison.Ordinal))
        {
            original.WriteLine(line);
            return;
        }

        LogTierCapture.Forward(sourceContext, level, line, "Console");
    }
}
