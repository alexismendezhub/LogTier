using System;
using System.Collections.Generic;
using System.Diagnostics.Tracing;
using System.Reflection;

namespace LogTier.Log4Net
{
    /// <summary>Settings for the log4net integration.</summary>
    public sealed class LogTierLog4NetOptions
    {
        /// <summary>Creates the options with default options.</summary>
        public LogTierLog4NetOptions()
        {
            var entry = Assembly.GetEntryAssembly();
            ApplicationName = entry != null ? entry.GetName().Name : "UnknownApplication";
            ApplicationVersion = entry != null && entry.GetName().Version != null ? entry.GetName().Version.ToString() : null;
            EnvironmentName = Environment.GetEnvironmentVariable("LOGTIER_ENVIRONMENT")
                ?? Environment.GetEnvironmentVariable("ASPNET_ENVIRONMENT")
                ?? "Unknown";

            HttpHeaderAllowList = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
            {
                "Accept", "Content-Type", "User-Agent", "X-Correlation-ID", "X-Request-ID",
                "X-Forwarded-For", "Forwarded", "Traceparent", "Tracestate"
            };

            RedactedHttpHeaders = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
            {
                "Authorization", "Proxy-Authorization", "Cookie", "Set-Cookie", "X-Api-Key", "X-API-Key"
            };
        }

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
        public long? AutonomousSystemNumber { get; set; }
        /// <summary>Capture the allow-listed HTTP headers.</summary>
        public bool IncludeHttpHeaders { get; set; } = true;
        /// <summary>HTTP headers allowed into the log.</summary>
        public ISet<string> HttpHeaderAllowList { get; private set; }
        /// <summary>Headers whose values are always replaced with [REDACTED].</summary>
        public ISet<string> RedactedHttpHeaders { get; private set; }
        /// <summary>Emit one completion event per HTTP request.</summary>
        public bool EmitHttpCompletionEvent { get; set; } = true;
        /// <summary>Capture source and destination IP addresses and ports.</summary>
        public bool IncludeNetworkEndpointData { get; set; } = true;
        /// <summary>Path of the rolling log file.</summary>
        public string LogFilePath { get; set; } = "logs\\logtier.jsonl";
        /// <summary>Size at which the log file rolls, for example 100MB.</summary>
        public string MaximumFileSize { get; set; } = "100MB";
        /// <summary>Number of rolled files kept.</summary>
        public int MaxSizeRollBackups { get; set; } = 30;
        /// <summary>Remove the root logger's existing appenders before adding the LogTier appender.</summary>
        public bool ReplaceExistingAppenders { get; set; }

        /// <summary>
        /// When true, automatic event type IDs also include the calling class and method. This walks the call
        /// stack on every event without an explicit type ID, so it is off by default.
        /// </summary>
        public bool IncludeLocationInEventTypeId { get; set; }

        /// <summary>Send System.Diagnostics.Trace and TraceSource output from any library to log4net. On by default.</summary>
        public bool CaptureTrace { get; set; } = true;

        /// <summary>
        /// Turn every line written to Console.Out (Info) and Console.Error (Warn) into a log4net event. On by default.
        /// Lines that already are LogTier JSON pass straight through.
        /// </summary>
        public bool CaptureConsole { get; set; } = true;

        /// <summary>Send EventSource events (Azure SDK, System.Net and others) to log4net. On by default.</summary>
        public bool CaptureEventSources { get; set; } = true;

        /// <summary>Lowest EventSource level captured. Warning by default, because Informational and Verbose are very chatty.</summary>
        public EventLevel EventSourceMinimumLevel { get; set; } = EventLevel.Warning;

        /// <summary>EventSources never captured: runtime counters and sources that would repeat events already logged.</summary>
        public ISet<string> ExcludedEventSources { get; } = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "Microsoft-Windows-DotNETRuntime",
            "System.Runtime",
            "System.Threading.Tasks.TplEventSource",
            "System.Diagnostics.Eventing.FrameworkEventSource",
            "Microsoft-Diagnostics-DiagnosticSource",
            "System.Diagnostics.Metrics",
            "Microsoft-Extensions-Logging",
            "System.Buffers.ArrayPoolEventSource",
            "System.Collections.Concurrent.ConcurrentCollectionsEventSource"
        };
    }
}
