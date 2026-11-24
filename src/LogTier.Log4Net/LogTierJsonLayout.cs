using System;
using System.Collections.Generic;
using System.Collections.Concurrent;
using System.Diagnostics;
using System.Diagnostics.Tracing;
using System.Globalization;
using System.IO;
using System.Net;
using System.Net.Sockets;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using log4net.Core;
using log4net.Layout;
using log4net.Util;
using Newtonsoft.Json;

namespace LogTier.Log4Net
{
    /// <summary>
    /// log4net layout that writes one M-21-31 EL1-aligned JSON object per event.
    /// The JSON is streamed straight to the appender's writer; no intermediate dictionaries are built per event.
    /// </summary>
    public sealed class LogTierJsonLayout : LayoutSkeleton
    {
        // Bounds the event type ID cache so dynamically named loggers cannot grow it without limit.
        private const int MaxCachedEventTypeIds = 10000;

        // Exceptions nested deeper than this are cut off so a self-referencing chain cannot loop.
        private const int MaxInnerExceptionDepth = 8;

        private static readonly string HostName = ResolveHostName();
        private static readonly int ProcessId = ResolveProcessId();

        private static readonly JsonSerializer ValueSerializer = JsonSerializer.Create(new JsonSerializerSettings
        {
            ReferenceLoopHandling = ReferenceLoopHandling.Ignore,
            Culture = CultureInfo.InvariantCulture
        });

        private readonly ConcurrentDictionary<string, string> _eventTypeIds = new ConcurrentDictionary<string, string>(StringComparer.Ordinal);

        /// <summary>Creates the layout with default options.</summary>
        public LogTierJsonLayout() : this(new LogTierLog4NetOptions()) { }

        /// <summary>Creates the layout with the given options.</summary>
        public LogTierJsonLayout(LogTierLog4NetOptions options)
        {
            if (options == null) throw new ArgumentNullException(nameof(options));
            SchemaVersion = options.SchemaVersion;
            ApplicationName = options.ApplicationName;
            ApplicationVersion = options.ApplicationVersion;
            EnvironmentName = options.EnvironmentName;
            DeviceIdentifier = options.DeviceIdentifier;
            DeviceMacAddress = options.DeviceMacAddress;
            AutonomousSystemNumber = options.AutonomousSystemNumber.HasValue
                ? options.AutonomousSystemNumber.Value.ToString(CultureInfo.InvariantCulture)
                : null;
            IncludeLocationInEventTypeId = options.IncludeLocationInEventTypeId;
            CaptureTrace = options.CaptureTrace;
            CaptureConsole = options.CaptureConsole;
            CaptureEventSources = options.CaptureEventSources;
            EventSourceMinimumLevel = options.EventSourceMinimumLevel;
            _excludedEventSources = options.ExcludedEventSources;
            IgnoresException = false;
        }

        private readonly ISet<string> _excludedEventSources;

        /// <summary>Schema version written to every event.</summary>
        public string SchemaVersion { get; set; } = "1.0.0";
        /// <summary>Application or service name.</summary>
        public string ApplicationName { get; set; }
        /// <summary>Application version.</summary>
        public string ApplicationVersion { get; set; }
        /// <summary>Deployment environment, for example Production.</summary>
        public string EnvironmentName { get; set; }
        /// <summary>Stable device identifier; the host name is used when empty.</summary>
        public string DeviceIdentifier { get; set; }
        /// <summary>Optional MAC address when agency policy requires it.</summary>
        public string DeviceMacAddress { get; set; }
        /// <summary>Optional autonomous system number.</summary>
        public string AutonomousSystemNumber { get; set; }
        /// <summary>When true, automatic event type IDs include the calling class and method (walks the stack per event).</summary>
        public bool IncludeLocationInEventTypeId { get; set; }
        /// <summary>Capture System.Diagnostics.Trace output from other libraries. On by default.</summary>
        public bool CaptureTrace { get; set; }
        /// <summary>Capture Console.Out and Console.Error output from other code. On by default.</summary>
        public bool CaptureConsole { get; set; }
        /// <summary>Capture EventSource events from other libraries. On by default.</summary>
        public bool CaptureEventSources { get; set; }
        /// <summary>Lowest EventSource level captured; Warning by default.</summary>
        public EventLevel EventSourceMinimumLevel { get; set; }
        /// <summary>Comma-separated EventSource names to leave out, added to the built-in exclusions.</summary>
        public string ExcludeEventSources { get; set; }

        /// <summary>Starts capturing other libraries' output (see <see cref="LogTierCapture"/>).</summary>
        public override void ActivateOptions()
        {
            var options = new LogTierLog4NetOptions
            {
                CaptureTrace = CaptureTrace,
                CaptureConsole = CaptureConsole,
                CaptureEventSources = CaptureEventSources,
                EventSourceMinimumLevel = EventSourceMinimumLevel
            };
            foreach (var name in _excludedEventSources) options.ExcludedEventSources.Add(name);
            if (!string.IsNullOrWhiteSpace(ExcludeEventSources))
            {
                foreach (var name in ExcludeEventSources.Split(new[] { ',' }, StringSplitOptions.RemoveEmptyEntries))
                    options.ExcludedEventSources.Add(name.Trim());
            }
            LogTierCapture.Start(options);
        }

        /// <inheritdoc />
        public override void Format(TextWriter writer, LoggingEvent loggingEvent)
        {
            if (writer == null) throw new ArgumentNullException(nameof(writer));
            if (loggingEvent == null) throw new ArgumentNullException(nameof(loggingEvent));

            var p = loggingEvent.GetProperties();
            var json = new JsonTextWriter(writer) { Formatting = Formatting.None, CloseOutput = false, Culture = CultureInfo.InvariantCulture };

            json.WriteStartObject();
            Write(json, "timestamp", loggingEvent.TimeStampUtc.ToString("yyyy-MM-dd'T'HH:mm:ss.fff'Z'", CultureInfo.InvariantCulture));

            json.WritePropertyName("m2131");
            json.WriteStartObject();
            Write(json, "memorandum", GetString(p, LogTierProperties.Memorandum) ?? LogTierStandard.Memorandum);
            Write(json, "event_logging_tier", GetString(p, LogTierProperties.EventLoggingTier) ?? LogTierStandard.EventLoggingTier);
            Write(json, "schema_version", GetString(p, LogTierProperties.SchemaVersion) ?? SchemaVersion ?? "1.0.0");
            json.WriteEndObject();

            json.WritePropertyName("event");
            json.WriteStartObject();
            Write(json, "id", GetString(p, LogTierProperties.EventId) ?? Guid.NewGuid().ToString("D"));
            Write(json, "type_id", GetString(p, LogTierProperties.EventTypeId) ?? GetEventTypeId(loggingEvent));
            Write(json, "category", GetValue(p, LogTierProperties.EventCategory));
            Write(json, "status_code", GetValue(p, LogTierProperties.EventStatusCode));
            Write(json, "level", loggingEvent.Level != null ? loggingEvent.Level.Name : null);
            Write(json, "message", loggingEvent.RenderedMessage);
            Write(json, "logger", loggingEvent.LoggerName);
            json.WriteEndObject();

            json.WritePropertyName("device");
            json.WriteStartObject();
            Write(json, "id", GetString(p, LogTierProperties.DeviceId) ?? NullIfBlank(DeviceIdentifier) ?? HostName);
            Write(json, "mac_address", GetValue(p, LogTierProperties.DeviceMac) ?? DeviceMacAddress);
            Write(json, "hostname", GetValue(p, LogTierProperties.HostName) ?? HostName);
            json.WriteEndObject();

            json.WritePropertyName("session");
            json.WriteStartObject();
            Write(json, "id", GetValue(p, LogTierProperties.SessionId));
            Write(json, "transaction_id", GetValue(p, LogTierProperties.TransactionId));
            Write(json, "trace_id", GetValue(p, LogTierProperties.TraceId));
            Write(json, "span_id", GetValue(p, LogTierProperties.SpanId));
            json.WriteEndObject();

            json.WritePropertyName("network");
            json.WriteStartObject();
            Write(json, "autonomous_system_number", GetValue(p, LogTierProperties.AutonomousSystemNumber) ?? ParseLong(AutonomousSystemNumber));
            json.WritePropertyName("source");
            json.WriteStartObject();
            Write(json, "ipv4", GetValue(p, LogTierProperties.SourceIpv4));
            Write(json, "ipv6", GetValue(p, LogTierProperties.SourceIpv6));
            Write(json, "port", GetValue(p, LogTierProperties.SourcePort));
            json.WriteEndObject();
            json.WritePropertyName("destination");
            json.WriteStartObject();
            Write(json, "ipv4", GetValue(p, LogTierProperties.DestinationIpv4));
            Write(json, "ipv6", GetValue(p, LogTierProperties.DestinationIpv6));
            Write(json, "port", GetValue(p, LogTierProperties.DestinationPort));
            json.WriteEndObject();
            json.WriteEndObject();

            json.WritePropertyName("http");
            json.WriteStartObject();
            Write(json, "method", GetValue(p, LogTierProperties.HttpMethod));
            Write(json, "scheme", GetValue(p, LogTierProperties.HttpScheme));
            Write(json, "host", GetValue(p, LogTierProperties.HttpHost));
            Write(json, "path", GetValue(p, LogTierProperties.HttpPath));
            Write(json, "status_code", GetValue(p, LogTierProperties.HttpStatusCode));
            Write(json, "response_time_ms", GetValue(p, LogTierProperties.ResponseTimeMs));
            Write(json, "headers", GetValue(p, LogTierProperties.HttpHeaders));
            json.WriteEndObject();

            json.WritePropertyName("user");
            json.WriteStartObject();
            Write(json, "id", GetValue(p, LogTierProperties.UserId));
            Write(json, "name", GetValue(p, LogTierProperties.UserName));
            json.WriteEndObject();

            Write(json, "command", GetValue(p, LogTierProperties.Command));

            json.WritePropertyName("application");
            json.WriteStartObject();
            Write(json, "name", GetValue(p, LogTierProperties.ApplicationName) ?? ApplicationName);
            Write(json, "version", GetValue(p, LogTierProperties.ApplicationVersion) ?? ApplicationVersion);
            Write(json, "environment", GetValue(p, LogTierProperties.EnvironmentName) ?? EnvironmentName);
            Write(json, "source_context", loggingEvent.LoggerName);
            Write(json, "process_id", GetValue(p, LogTierProperties.ProcessId) ?? ProcessId);
            Write(json, "thread_id", GetValue(p, LogTierProperties.ThreadId) ?? Thread.CurrentThread.ManagedThreadId);
            json.WriteEndObject();

            json.WritePropertyName("exception");
            if (loggingEvent.ExceptionObject != null) WriteException(json, loggingEvent.ExceptionObject, 0);
            else
            {
                var exceptionText = loggingEvent.GetExceptionString();
                if (string.IsNullOrWhiteSpace(exceptionText)) json.WriteNull();
                else
                {
                    json.WriteStartObject();
                    Write(json, "text", exceptionText);
                    json.WriteEndObject();
                }
            }

            json.WritePropertyName("properties");
            json.WriteStartObject();
            foreach (var entry in p)
            {
                var key = entry.Key as string;
                // log4net:* entries repeat the host name and expose the Windows account the process runs as.
                if (string.IsNullOrEmpty(key)
                    || key.StartsWith("LogTier", StringComparison.Ordinal)
                    || key.StartsWith("log4net:", StringComparison.Ordinal)) continue;
                Write(json, key, entry.Value);
            }
            json.WriteEndObject();

            json.WriteEndObject();
            json.Flush();
            writer.Write(Environment.NewLine);
        }

        private static void WriteException(JsonTextWriter json, Exception exception, int depth)
        {
            json.WriteStartObject();
            Write(json, "type", exception.GetType().FullName);
            Write(json, "message", exception.Message);
            Write(json, "stack_trace", exception.StackTrace);
            if (exception.InnerException != null && depth < MaxInnerExceptionDepth)
            {
                json.WritePropertyName("inner");
                WriteException(json, exception.InnerException, depth + 1);
            }
            json.WriteEndObject();
        }

        private static void Write(JsonTextWriter json, string name, object value)
        {
            json.WritePropertyName(name);
            if (value == null) json.WriteNull();
            else if (value is string text) json.WriteValue(text);
            else ValueSerializer.Serialize(json, value);
        }

        private string GetEventTypeId(LoggingEvent loggingEvent)
        {
            var source = (loggingEvent.LoggerName ?? string.Empty) + "|" + (loggingEvent.Level != null ? loggingEvent.Level.Name : string.Empty);
            if (IncludeLocationInEventTypeId)
            {
                var location = loggingEvent.LocationInformation;
                if (location != null) source += "|" + location.ClassName + "|" + location.MethodName;
            }

            string cached;
            if (_eventTypeIds.TryGetValue(source, out cached)) return cached;

            var typeId = CreateStableEventTypeId(source);
            if (_eventTypeIds.Count < MaxCachedEventTypeIds) _eventTypeIds.TryAdd(source, typeId);
            return typeId;
        }

        private static string CreateStableEventTypeId(string source)
        {
            using (var sha = SHA256.Create())
            {
                var digest = sha.ComputeHash(Encoding.UTF8.GetBytes(source));
                return "AUTO-" + BitConverter.ToString(digest, 0, 8).Replace("-", string.Empty);
            }
        }

        private static object GetValue(PropertiesDictionary properties, string name)
        {
            return properties.Contains(name) ? properties[name] : null;
        }

        private static string GetString(PropertiesDictionary properties, string name)
        {
            var value = GetValue(properties, name);
            return value != null ? Convert.ToString(value, CultureInfo.InvariantCulture) : null;
        }

        private static string NullIfBlank(string value) => string.IsNullOrWhiteSpace(value) ? null : value;

        private static object ParseLong(string value)
        {
            long parsed;
            return long.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out parsed) ? (object)parsed : null;
        }

        private static string ResolveHostName()
        {
            try { return Dns.GetHostName(); }
            catch (SocketException) { return Environment.MachineName; }
        }

        private static int ResolveProcessId()
        {
            using (var process = Process.GetCurrentProcess()) return process.Id;
        }
    }
}
