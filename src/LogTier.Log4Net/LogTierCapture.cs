using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.Diagnostics.Tracing;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using log4net;
using log4net.Core;

namespace LogTier.Log4Net
{
    /// <summary>
    /// Captures what other libraries write outside log4net (System.Diagnostics.Trace, EventSource, Console)
    /// and rewrites it through log4net so it comes out in the LogTier JSON format.
    /// Started automatically by <see cref="LogTierJsonLayout"/> and <see cref="LogTierLog4NetConfigurator"/>.
    /// </summary>
    public static class LogTierCapture
    {
        private static readonly object Gate = new object();
        private static LogTierTraceListener _traceListener;
        private static LogTierEventSourceListener _eventSourceListener;
        private static TextWriter _originalOut;
        private static TextWriter _originalError;

        /// <summary>True while capture is running.</summary>
        public static bool IsStarted { get; private set; }

        /// <summary>Starts the captures switched on in <paramref name="options"/>. Only the first call has an effect.</summary>
        public static void Start(LogTierLog4NetOptions options)
        {
            if (options == null) throw new ArgumentNullException(nameof(options));
            lock (Gate)
            {
                if (IsStarted) return;
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
                    Console.SetOut(new LogTierConsoleWriter(_originalOut, "Console", Level.Info));
                    Console.SetError(new LogTierConsoleWriter(_originalError, "Console.Error", Level.Warn));
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
                if (!IsStarted) return;
                IsStarted = false;

                if (_traceListener != null)
                {
                    Trace.Listeners.Remove(_traceListener);
                    _traceListener.Dispose();
                    _traceListener = null;
                }

                if (_originalOut != null)
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

                if (_eventSourceListener != null)
                {
                    _eventSourceListener.Dispose();
                    _eventSourceListener = null;
                }
            }
        }

        // Writes done while a captured message is being logged go straight to the original output,
        // so an appender that writes to the console or to Trace can never feed itself.
        [ThreadStatic]
        private static bool _forwarding;

        internal static bool IsForwarding => _forwarding;

        internal static void Forward(string loggerName, Level level, string message, string captureSource, IDictionary<string, object> properties = null)
        {
            if (_forwarding || string.IsNullOrWhiteSpace(message)) return;
            _forwarding = true;
            try
            {
                var all = properties != null
                    ? new Dictionary<string, object>(properties, StringComparer.Ordinal)
                    : new Dictionary<string, object>(StringComparer.Ordinal);
                all["CaptureSource"] = captureSource;
                using (LogTierContext.Push(all))
                {
                    var logger = LogManager.GetLogger(loggerName).Logger;
                    if (logger.IsEnabledFor(level)) logger.Log(typeof(LogTierCapture), level, message, null);
                }
            }
            finally
            {
                _forwarding = false;
            }
        }
    }

    /// <summary>Trace listener that sends System.Diagnostics.Trace and TraceSource output to log4net.</summary>
    public sealed class LogTierTraceListener : TraceListener
    {
        private readonly StringBuilder _pending = new StringBuilder();

        /// <summary>Creates the listener.</summary>
        public LogTierTraceListener() : base("LogTier") { }

        /// <inheritdoc />
        public override bool IsThreadSafe => false;

        /// <inheritdoc />
        public override void Write(string message)
        {
            if (LogTierCapture.IsForwarding) return;
            lock (_pending) _pending.Append(message);
        }

        /// <inheritdoc />
        public override void WriteLine(string message)
        {
            if (LogTierCapture.IsForwarding) return;
            string line;
            lock (_pending)
            {
                _pending.Append(message);
                line = _pending.ToString();
                _pending.Clear();
            }
            LogTierCapture.Forward("Trace", Level.Debug, line, "Trace");
        }

        /// <inheritdoc />
        public override void TraceEvent(TraceEventCache eventCache, string source, TraceEventType eventType, int id, string message)
        {
            if (LogTierCapture.IsForwarding || (Filter != null && !Filter.ShouldTrace(eventCache, source, eventType, id, message, null, null, null))) return;
            var properties = new Dictionary<string, object> { ["TraceSource"] = source, ["TraceEventId"] = id };
            LogTierCapture.Forward(string.IsNullOrEmpty(source) ? "Trace" : "Trace." + source, Map(eventType), message, "Trace", properties);
        }

        /// <inheritdoc />
        public override void TraceEvent(TraceEventCache eventCache, string source, TraceEventType eventType, int id, string format, params object[] args)
        {
            var message = args == null || args.Length == 0 ? format : string.Format(CultureInfo.InvariantCulture, format, args);
            TraceEvent(eventCache, source, eventType, id, message);
        }

        /// <inheritdoc />
        public override void TraceData(TraceEventCache eventCache, string source, TraceEventType eventType, int id, object data)
        {
            TraceEvent(eventCache, source, eventType, id, Convert.ToString(data, CultureInfo.InvariantCulture));
        }

        /// <inheritdoc />
        public override void TraceData(TraceEventCache eventCache, string source, TraceEventType eventType, int id, params object[] data)
        {
            var text = data == null ? null : string.Join(", ", data.Select(d => Convert.ToString(d, CultureInfo.InvariantCulture)));
            TraceEvent(eventCache, source, eventType, id, text);
        }

        /// <inheritdoc />
        public override void Fail(string message, string detailMessage)
        {
            LogTierCapture.Forward("Trace", Level.Error, string.IsNullOrEmpty(detailMessage) ? message : message + " " + detailMessage, "Trace");
        }

        private static Level Map(TraceEventType eventType)
        {
            switch (eventType)
            {
                case TraceEventType.Critical: return Level.Fatal;
                case TraceEventType.Error: return Level.Error;
                case TraceEventType.Warning: return Level.Warn;
                case TraceEventType.Information: return Level.Info;
                default: return Level.Debug;
            }
        }
    }

    /// <summary>Listens to EventSource events (Azure SDK, System.Net and others) and sends them to log4net.</summary>
    public sealed class LogTierEventSourceListener : EventListener
    {
        // Sources created before the constructor body runs are queued here; field initializers run before the base constructor.
        private readonly List<EventSource> _pendingSources = new List<EventSource>();
        private readonly EventLevel _level;
        private readonly HashSet<string> _excluded;
        private readonly bool _ready;

        /// <summary>Creates the listener for every EventSource except <paramref name="excludedSources"/>.</summary>
        public LogTierEventSourceListener(EventLevel level, IEnumerable<string> excludedSources)
        {
            _level = level;
            _excluded = new HashSet<string>(excludedSources ?? Enumerable.Empty<string>(), StringComparer.OrdinalIgnoreCase);
            List<EventSource> pending;
            lock (_pendingSources)
            {
                _ready = true;
                pending = _pendingSources.ToList();
                _pendingSources.Clear();
            }
            foreach (var source in pending) Enable(source);
        }

        /// <inheritdoc />
        protected override void OnEventSourceCreated(EventSource eventSource)
        {
            if (_pendingSources == null) return;
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
            if (_excluded.Contains(eventSource.Name)) return;
            EnableEvents(eventSource, _level, EventKeywords.All);
        }

        /// <inheritdoc />
        protected override void OnEventWritten(EventWrittenEventArgs eventData)
        {
            if (eventData == null || LogTierCapture.IsForwarding || !_ready || _excluded.Contains(eventData.EventSource.Name)) return;

            var payload = new Dictionary<string, object>(StringComparer.Ordinal);
            if (eventData.PayloadNames != null && eventData.Payload != null)
            {
                for (var i = 0; i < eventData.PayloadNames.Count && i < eventData.Payload.Count; i++)
                    payload[eventData.PayloadNames[i]] = eventData.Payload[i];
            }

            var properties = new Dictionary<string, object>
            {
                ["EventSourceName"] = eventData.EventSource.Name,
                ["EventSourceEventName"] = eventData.EventName,
                ["EventSourceEventId"] = eventData.EventId,
                ["EventSourcePayload"] = payload
            };
            LogTierCapture.Forward("EventSource." + eventData.EventSource.Name, Map(eventData.Level), FormatMessage(eventData), "EventSource", properties);
        }

        private static string FormatMessage(EventWrittenEventArgs eventData)
        {
            var payload = eventData.Payload ?? new ReadOnlyCollection<object>(new object[0]);
            if (!string.IsNullOrEmpty(eventData.Message))
            {
                try { return string.Format(CultureInfo.InvariantCulture, eventData.Message, payload.ToArray()); }
                catch (FormatException) { return eventData.Message; }
            }

            var names = eventData.PayloadNames;
            if (names == null || names.Count == 0) return eventData.EventName;
            return eventData.EventName + " " + string.Join(", ", names.Select((n, i) => n + "=" + Convert.ToString(i < payload.Count ? payload[i] : null, CultureInfo.InvariantCulture)));
        }

        private static Level Map(EventLevel level)
        {
            switch (level)
            {
                case EventLevel.Critical: return Level.Fatal;
                case EventLevel.Error: return Level.Error;
                case EventLevel.Warning: return Level.Warn;
                case EventLevel.Informational:
                case EventLevel.LogAlways: return Level.Info;
                default: return Level.Debug;
            }
        }
    }

    /// <summary>Console writer that turns each line other code writes into a log4net event.</summary>
    internal sealed class LogTierConsoleWriter : TextWriter
    {
        // Lines that already are LogTier JSON (a console appender with the LogTier layout) pass straight through.
        private const string LogTierJsonPrefix = "{\"timestamp\":";

        private readonly TextWriter _original;
        private readonly string _loggerName;
        private readonly Level _level;
        private readonly StringBuilder _line = new StringBuilder();

        public LogTierConsoleWriter(TextWriter original, string loggerName, Level level)
        {
            _original = original;
            _loggerName = loggerName;
            _level = level;
        }

        public override Encoding Encoding => _original.Encoding;

        public override void Write(char value)
        {
            if (LogTierCapture.IsForwarding)
            {
                _original.Write(value);
                return;
            }

            string completed = null;
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

            if (completed != null) Emit(completed);
        }

        public override void Write(string value)
        {
            if (value == null) return;
            if (LogTierCapture.IsForwarding || IsWholeLogTierLine(value))
            {
                _original.Write(value);
                return;
            }
            foreach (var c in value) Write(c);
        }

        public override void Write(char[] buffer, int index, int count)
        {
            if (buffer == null) return;
            if (LogTierCapture.IsForwarding)
            {
                _original.Write(buffer, index, count);
                return;
            }
            for (var i = index; i < index + count; i++) Write(buffer[i]);
        }

        public override void Flush()
        {
            string partial;
            lock (_line)
            {
                partial = _line.Length > 0 ? _line.ToString() : null;
                _line.Clear();
            }
            if (partial != null) Emit(partial);
            _original.Flush();
        }

        // Fast path for a complete LogTier JSON line written in one call, the way a LogTier console appender writes.
        private bool IsWholeLogTierLine(string value)
        {
            if (!value.StartsWith(LogTierJsonPrefix, StringComparison.Ordinal) || value.IndexOf('\n') != value.Length - 1) return false;
            lock (_line) return _line.Length == 0;
        }

        private void Emit(string line)
        {
            if (line.Length == 0) return;
            if (line.StartsWith(LogTierJsonPrefix, StringComparison.Ordinal))
            {
                _original.WriteLine(line);
                return;
            }
            LogTierCapture.Forward(_loggerName, _level, line, "Console");
        }
    }
}
