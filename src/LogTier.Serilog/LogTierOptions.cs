using System.Diagnostics.Tracing;
using System.Reflection;

namespace LogTier.Serilog;

/// <summary>
/// Configuration for M-21-31 structured logging.
/// </summary>
public sealed class LogTierOptions
{
    /// <summary>Schema version defined by this package.</summary>
    public string SchemaVersion { get; set; } = "1.0.0";

    /// <summary>Application/service name. Defaults to the entry assembly name.</summary>
    public string ApplicationName { get; set; } =
        Assembly.GetEntryAssembly()?.GetName().Name ?? "UnknownApplication";

    /// <summary>Application version. Defaults to the entry assembly version.</summary>
    public string? ApplicationVersion { get; set; } =
        Assembly.GetEntryAssembly()?.GetName().Version?.ToString();

    /// <summary>Deployment environment, for example Development, Test, Staging, Production.</summary>
    public string EnvironmentName { get; set; } =
        Environment.GetEnvironmentVariable("ASPNETCORE_ENVIRONMENT")
        ?? Environment.GetEnvironmentVariable("DOTNET_ENVIRONMENT")
        ?? "Unknown";

    /// <summary>
    /// Stable device identifier. M-21-31 permits a MAC address or other unique identifier.
    /// If omitted, the host name is used as the device identifier.
    /// </summary>
    public string? DeviceIdentifier { get; set; }

    /// <summary>Optional MAC address when agency policy requires it.</summary>
    public string? DeviceMacAddress { get; set; }

    /// <summary>Optional autonomous system number when known/applicable.</summary>
    public long? AutonomousSystemNumber { get; set; }

    /// <summary>Capture a safe allow-list of HTTP headers.</summary>
    public bool IncludeHttpHeaders { get; set; } = true;

    /// <summary>
    /// HTTP headers allowed into the log. Sensitive headers remain redacted even if added here.
    /// </summary>
    public ISet<string> HttpHeaderAllowList { get; } = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
    {
        "Accept",
        "Content-Type",
        "User-Agent",
        "X-Correlation-ID",
        "X-Request-ID",
        "X-Forwarded-For",
        "Forwarded",
        "Traceparent",
        "Tracestate"
    };

    /// <summary>Headers that are always redacted.</summary>
    public ISet<string> RedactedHttpHeaders { get; } = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
    {
        "Authorization",
        "Proxy-Authorization",
        "Cookie",
        "Set-Cookie",
        "X-Api-Key",
        "X-API-Key"
    };

    /// <summary>
    /// When true, the package emits one standardized completion event per ASP.NET Core request.
    /// </summary>
    public bool EmitHttpCompletionEvent { get; set; } = true;

    /// <summary>
    /// When true, source/destination IPs and ports are captured for ASP.NET Core requests.
    /// </summary>
    public bool IncludeNetworkEndpointData { get; set; } = true;

    /// <summary>Send System.Diagnostics.Trace and TraceSource output from any library to Serilog. On by default.</summary>
    public bool CaptureTrace { get; set; } = true;

    /// <summary>
    /// Turn every line written to Console.Out (Information) and Console.Error (Warning) into a Serilog event. On by default.
    /// Lines that already are LogTier JSON pass straight through.
    /// </summary>
    public bool CaptureConsole { get; set; } = true;

    /// <summary>Send EventSource events (Azure SDK, System.Net, Kestrel and others) to Serilog. On by default.</summary>
    public bool CaptureEventSources { get; set; } = true;

    /// <summary>Lowest EventSource level captured. Warning by default, because Informational and Verbose are very chatty.</summary>
    public EventLevel EventSourceMinimumLevel { get; set; } = EventLevel.Warning;

    /// <summary>EventSources never captured: runtime counters and sources that would repeat events already logged.</summary>
    public ISet<string> ExcludedEventSources { get; } = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
    {
        "Microsoft-Windows-DotNETRuntime",
        "System.Runtime",
        "System.Threading.Tasks.TplEventSource",
        "Microsoft-Diagnostics-DiagnosticSource",
        "System.Diagnostics.Metrics",
        "Microsoft-Extensions-Logging",
        "System.Buffers.ArrayPoolEventSource",
        "System.Collections.Concurrent.ConcurrentCollectionsEventSource",
        "Microsoft.AspNetCore.Hosting",
        "Microsoft-AspNetCore-Server-Kestrel",
        "Microsoft.AspNetCore.Http.Connections",
        "System.Net.NameResolution",
        "System.Net.Sockets"
    };
}
