# LogTier: reference

Everything beyond the README: how it is built, how to use and configure it, the API and the tests.

## 1. What it does
### 1.1 Name and standard

The standard is **OMB memorandum M-21-31** (*Improving the Federal Government's Investigative and Remediation Capabilities Related to Cybersecurity Incidents*, August 2021). **EL1** is not a separate standard: it is the first of the memorandum's event logging maturity tiers (EL0 Not Effective, EL1 Basic, EL2 Intermediate, EL3 Advanced). LogTier targets the application data required at EL1, so every event carries both values: `"memorandum": "M-21-31"` and `"event_logging_tier": "EL1"`.

### 1.2 Packages

| NuGet package | Target runtime | Primary use |
|---|---|---|
| `LogTier.Serilog` | .NET 6, .NET 8, .NET 10 | ASP.NET Core, APIs, workers, services, console applications |
| `LogTier.Log4Net` | .NET Framework 4.8 | Classic ASP.NET (`System.Web`), Windows Services, console/batch applications |

Current package baseline: **1.1.0**.

> **Important compliance boundary**
>
> These packages standardize application event data and help applications emit an EL1-aligned logging schema. Installing a package by itself does **not** make an application, information system, or agency fully M-21-31/EL1 compliant. Infrastructure controls such as centralized forwarding, secure transport, access control, integrity protection, retention, time synchronization, monitoring for logging failures, and other required telemetry sources remain operational responsibilities.

### 1.3 What every event records

Both integrations emit the same top-level JSON structure and populate applicable fields such as:

- UTC timestamp with millisecond precision;
- M-21-31 memorandum identifier;
- event logging tier (`EL1`);
- schema version;
- unique event ID;
- stable event type ID;
- event category and status;
- device/host identifier;
- session ID;
- transaction/correlation ID;
- trace ID and span ID when available;
- source and destination IPv4/IPv6 addresses and ports when applicable;
- autonomous system number when supplied;
- HTTP method, scheme, host, path, status, and response time;
- allow-listed HTTP headers;
- user ID and username when available;
- command/operation executed;
- application, process, and thread metadata;
- exception details;
- original structured/custom properties.

It also captures, by default, what other libraries write outside Serilog or log4net (Trace, Console, EventSource and Microsoft.Extensions.Logging) and writes it in the same format; see [section 9](#9-output-from-other-libraries-on-by-default).

See [`docs/SCHEMA.md`](SCHEMA.md) for the shared field contract.

## 2. Project structure

LogTier is a library, so it has no Domain / Application / Infrastructure / Server layers: each package is one project.

```text
LogTier.slnx                                  ← solution (.slnx format)
├─ src/
│  ├─ LogTier.Serilog/                        ← NuGet package for .NET 6, 8 and 10 (formatter, enrichers, middleware, captures)
│  ├─ LogTier.Log4Net/                        ← NuGet package for .NET Framework 4.8 (layout, HTTP module, captures)
│  └─ tests/
│     ├─ LogTier.Serilog.IntegrationTests/    ← the whole Serilog pipeline into the LogTier JSON (.NET 10)
│     └─ LogTier.Log4Net.IntegrationTests/    ← the whole log4net pipeline into the LogTier JSON (.NET Framework 4.8)
├─ samples/
│  ├─ LogTier.Serilog.Sample/                 ← ASP.NET Core sample
│  └─ LogTier.Log4Net.Sample/                 ← .NET Framework 4.8 sample with web.config example
├─ nuget/                                     ← current packages; doubles as a local NuGet feed
├─ docs/                                      ← SCHEMA.md (field contract), Word and PDF copies of this README
├─ scripts/                                   ← build-and-test.cmd (build + tests), pack.cmd / pack.ps1 (build, tests, packages)
├─ Directory.Build.props                      ← shared build settings and package metadata
├─ global.json
└─ LICENSE
```

## 3. Consumer setup — `LogTier.Serilog`

### 3.1 Prerequisites

Use one of the supported target frameworks:

- .NET 6
- .NET 8
- .NET 10

The package supplies the LogTier formatter/enricher and uses Serilog as the logging provider.

### 3.2 Install from NuGet

Using the .NET CLI:

```bash
dotnet add package LogTier.Serilog --version 1.1.0
```

Using Visual Studio Package Manager Console:

```powershell
Install-Package LogTier.Serilog -Version 1.1.0
```

Using `PackageReference` directly:

```xml
<ItemGroup>
  <PackageReference Include="LogTier.Serilog" Version="1.1.0" />
</ItemGroup>
```

### 3.3 Minimal ASP.NET Core setup

Replace or update `Program.cs`:

```csharp
using LogTier.Serilog;
using Serilog;

var builder = WebApplication.CreateBuilder(args);

var logTier = new LogTierOptions
{
    ApplicationName = "Orders.Api",
    ApplicationVersion = "1.0.0",
    EnvironmentName = builder.Environment.EnvironmentName,

    // Prefer a stable enterprise asset/device identifier when available.
    // If omitted, the package falls back to host information where applicable.
    DeviceIdentifier = Environment.GetEnvironmentVariable("LOGTIER_DEVICE_ID"),

    IncludeHttpHeaders = true,
    IncludeNetworkEndpointData = true,
    EmitHttpCompletionEvent = true
};

builder.Host.UseLogTierSerilog(
    logTier,
    (_, logger) => logger
        .MinimumLevel.Information()
        .WriteTo.LogTierConsole(logTier)
        .WriteTo.LogTierFile(
            logTier,
            path: "logs/logtier-.jsonl",
            retainedFileCountLimit: 30));

var app = builder.Build();

// If the application is behind a trusted reverse proxy or load balancer,
// configure ASP.NET Core forwarded headers BEFORE this middleware.
app.UseLogTierRequestLogging(logTier);

Log.Logger.LogTierApplicationStarted();

app.MapGet("/health", () => Results.Ok(new { status = "ok" }));

try
{
    await app.RunAsync();
}
finally
{
    Log.Logger.LogTierApplicationStopped();
    await Log.CloseAndFlushAsync();
}
```

`UseLogTierSerilog()` configures Serilog as the `Microsoft.Extensions.Logging` provider. Normal application logging such as `ILogger<T>.LogInformation(...)` therefore flows through Serilog and receives the LogTier enricher.

### 3.4 Using `ILogger<T>` normally

No custom logger interface is required in application code:

```csharp
public sealed class OrderService
{
    private readonly ILogger<OrderService> _logger;

    public OrderService(ILogger<OrderService> logger)
    {
        _logger = logger;
    }

    public void ProcessOrder(string orderId)
    {
        _logger.LogInformation("Processing order {OrderId}", orderId);
    }
}
```

Because Serilog is the provider, the event is normalized by `LogTier.Serilog` when written through an LogTier formatter sink.

### 3.5 Recommended `appsettings.json`

LogTier-specific options are passed through `LogTierOptions`.

Levels are set on the Serilog configuration (`.MinimumLevel...` in the `UseLogTierSerilog` callback). Once Serilog is the provider, the Microsoft `"Logging:LogLevel"` section is **not** applied; to keep levels in `appsettings.json`, use a `"Serilog"` section and call `logger.ReadFrom.Configuration(context.Configuration)` in the callback (`Serilog.Settings.Configuration`, included with `Serilog.AspNetCore`).

Example:

```json
{
  "Serilog": {
    "MinimumLevel": {
      "Default": "Information",
      "Override": { "Microsoft.AspNetCore": "Warning" }
    }
  },
  "LogTier": {
    "ApplicationName": "Orders.Api",
    "ApplicationVersion": "1.0.0",
    "DeviceIdentifier": "api-node-001",
    "IncludeHttpHeaders": true,
    "IncludeNetworkEndpointData": true,
    "EmitHttpCompletionEvent": true
  }
}
```

If the application binds configuration manually, map these values into `LogTierOptions` during startup.

### 3.6 Standard security/audit event helpers

```csharp
Log.Logger.LogTierAuthenticationAttempt(
    succeeded: true,
    userId: "user-123",
    userName: "jsmith");

Log.Logger.LogTierAccountChanged(
    changeType: "PrivilegeAssignment",
    affectedUserId: "user-123",
    actingUserId: "admin-7");

Log.Logger.LogTierPrivilegeUsed(
    privilege: "ExportAuditLogs",
    userId: "admin-7",
    succeeded: true);

Log.Logger.LogTierConfigurationChanged(
    setting: "Security.SessionTimeout",
    actingUserId: "admin-7");

Log.Logger.LogTierTransaction(
    transactionType: "CaseUpdate",
    transactionId: "txn-20260907-001",
    statusCode: "Success",
    userId: "user-123");
```

For audit-relevant domain events, use a stable application-defined event type ID rather than relying only on automatically generated IDs.

### 3.7 Reverse proxy / load balancer setup

If the application runs behind IIS, Azure Application Gateway, AWS ALB, NGINX, YARP, or another trusted proxy, configure ASP.NET Core forwarded headers before `UseLogTierRequestLogging()` so the application sees the intended client/network data.

Example:

```csharp
using Microsoft.AspNetCore.HttpOverrides;

builder.Services.Configure<ForwardedHeadersOptions>(options =>
{
    options.ForwardedHeaders =
        ForwardedHeaders.XForwardedFor |
        ForwardedHeaders.XForwardedProto;

    // Production deployments should explicitly configure trusted proxies/networks.
});

var app = builder.Build();

app.UseForwardedHeaders();
app.UseLogTierRequestLogging(logTier);
```

Do not trust forwarded headers from arbitrary untrusted clients.

## 4. Consumer setup — `LogTier.Log4Net` for .NET Framework 4.8

### 4.1 Prerequisites

Target:

```xml
<TargetFrameworkVersion>v4.8</TargetFrameworkVersion>
```

or, for SDK-style .NET Framework projects:

```xml
<TargetFramework>net48</TargetFramework>
```

The package depends on log4net and Newtonsoft.Json.

### 4.2 Install from NuGet

Visual Studio Package Manager Console:

```powershell
Install-Package LogTier.Log4Net -Version 1.1.0
```

`PackageReference`:

```xml
<ItemGroup>
  <PackageReference Include="LogTier.Log4Net" Version="1.1.0" />
</ItemGroup>
```

## 5. .NET Framework 4.8 setup — existing log4net application

Use this path when the application already has a log4net configuration and you want existing `log.Info(...)`, `log.Warn(...)`, and `log.Error(...)` calls to produce the normalized LogTier JSON structure.

### 5.1 Configure the appender in `web.config` or `app.config`

```xml
<configuration>
  <configSections>
    <section name="log4net"
             type="log4net.Config.Log4NetConfigurationSectionHandler, log4net" />
  </configSections>

  <log4net>
    <appender name="LogTierFile" type="log4net.Appender.RollingFileAppender">
      <file value="C:\Logs\legacy-logtier.jsonl" />
      <appendToFile value="true" />
      <rollingStyle value="Composite" />
      <datePattern value=".yyyy-MM-dd" />
      <maximumFileSize value="100MB" />
      <maxSizeRollBackups value="30" />
      <staticLogFileName value="true" />

      <layout type="LogTier.Log4Net.LogTierJsonLayout, LogTier.Log4Net">
        <ApplicationName value="Legacy.CaseManagement" />
        <ApplicationVersion value="12.4.0" />
        <EnvironmentName value="Production" />
        <DeviceIdentifier value="server-asset-001" />
      </layout>
    </appender>

    <root>
      <level value="INFO" />
      <appender-ref ref="LogTierFile" />
    </root>
  </log4net>
</configuration>
```

### 5.2 Initialize log4net

For classic ASP.NET, add this to `Global.asax.cs` if the application does not already initialize log4net:

```csharp
using System.Web;
using log4net.Config;

public class MvcApplication : HttpApplication
{
    protected void Application_Start()
    {
        XmlConfigurator.Configure();

        // Existing MVC/Web API/other startup configuration follows here.
    }
}
```

If the application already calls `XmlConfigurator.Configure()` or uses an assembly-level log4net configuration attribute, do not initialize it twice.

### 5.3 Existing application logging continues to work

```csharp
using log4net;

public sealed class CaseService
{
    private static readonly ILog Log = LogManager.GetLogger(typeof(CaseService));

    public void UpdateCase(string caseId)
    {
        Log.Info("Updating case " + caseId);
    }
}
```

The LogTier layout converts the event to the shared JSONL contract.

## 6. Classic ASP.NET 4.8 automatic HTTP request enrichment

`LogTier.Log4Net` includes `LogTierHttpModule` for `System.Web` applications.

The module automatically adds request-scoped properties to log4net `LogicalThreadContext`, including transaction ID, authenticated user, HTTP route data, source/destination endpoint data, status code, and response time when available.

It also emits one standardized HTTP completion event at the end of the request.

### 6.1 Add LogTier application settings

Add these settings to `web.config`:

```xml
<configuration>
  <appSettings>
    <add key="LogTier:ApplicationName" value="Legacy.Web" />
    <add key="LogTier:ApplicationVersion" value="5.7.0" />
    <add key="LogTier:EnvironmentName" value="Production" />
    <add key="LogTier:DeviceIdentifier" value="iis-node-01" />

    <add key="LogTier:IncludeHttpHeaders" value="true" />
    <add key="LogTier:IncludeNetworkEndpointData" value="true" />
    <add key="LogTier:EmitHttpCompletionEvent" value="true" />

    <add key="LogTier:HeaderAllowList"
         value="Accept,Content-Type,User-Agent,X-Correlation-ID,X-Request-ID,X-Forwarded-For,Forwarded,Traceparent,Tracestate" />
  </appSettings>

  <system.webServer>
    <modules runAllManagedModulesForAllRequests="true">
      <add name="LogTierHttpModule"
           type="LogTier.Log4Net.LogTierHttpModule, LogTier.Log4Net" />
    </modules>
  </system.webServer>
</configuration>
```

For IIS classic pipeline scenarios that require registration under `system.web/httpModules`, add the equivalent module registration required by that application's hosting model. Integrated pipeline is the recommended configuration for normal modern IIS-hosted .NET Framework 4.8 applications.

### 6.2 Result

Application code remains unchanged:

```csharp
Log.Info("Case retrieved");
```

During an HTTP request, that event inherits the available request context automatically.

At request completion, the module emits an event using:

```text
APP-HTTP-REQUEST-COMPLETED
```

with the HTTP status and response time.

## 7. .NET Framework 4.8 Windows Service / console / batch setup

For a process that does not use `System.Web`, use the programmatic configurator.

```csharp
using LogTier.Log4Net;

internal static class Program
{
    private static void Main()
    {
        var log = LogTierLog4NetConfigurator.ConfigureRollingFile(
            new LogTierLog4NetOptions
            {
                ApplicationName = "Legacy.BatchProcessor",
                ApplicationVersion = "3.2.0",
                EnvironmentName = "Production",
                DeviceIdentifier = "batch-node-01",
                LogFilePath = @"C:\Logs\batch-logtier.jsonl",
                MaximumFileSize = "100MB",
                MaxSizeRollBackups = 30
            });

        log.LogTierApplicationStarted();

        try
        {
            log.Info("Batch processing started");

            // Application work here.

            log.LogTierTransaction(
                transactionType: "NightlyBatch",
                transactionId: Guid.NewGuid().ToString("D"),
                statusCode: "Success");
        }
        catch (Exception ex)
        {
            log.LogTierApplicationFailure(ex, "NightlyBatch");
            throw;
        }
        finally
        {
            log.LogTierApplicationStopped();
            log4net.LogManager.Shutdown();
        }
    }
}
```

## 8. log4net standard security/audit event helpers

```csharp
log.LogTierAuthenticationAttempt(true, "user-123", "jsmith");

log.LogTierAccountChanged(
    "PrivilegeAssignment",
    "user-123",
    "admin-7");

log.LogTierPrivilegeUsed(
    "ExportAuditLogs",
    "admin-7",
    true);

log.LogTierConfigurationChanged(
    "Security.SessionTimeout",
    "admin-7");

log.LogTierTransaction(
    "CaseUpdate",
    "txn-20260907-001",
    "Success",
    "user-123");
```

Custom event:

```csharp
log.LogTierInfo(
    eventTypeId: "CASE-APPROVED",
    category: "BusinessTransaction",
    statusCode: "Success",
    message: "Case approved",
    properties: new Dictionary<string, object>
    {
        ["CaseId"] = caseId
    });
```

Use stable event type IDs for security- or audit-relevant domain events.

## 9. Output from other libraries (on by default)

Libraries do not all log through Serilog or log4net. LogTier captures the other common channels and rewrites them through the same pipeline, so they come out in the LogTier JSON format with the same device, application and request context. Each captured event carries `properties.CaptureSource` (`Trace`, `Console`, `EventSource` or `MicrosoftExtensionsLogging`).

| Channel | Who writes there | Logger / source context | Level |
|---|---|---|---|
| `System.Diagnostics.Trace` (`Trace.WriteLine`, `Trace.TraceError`, `Debug` in debug builds) | Older libraries, WCF, many .NET Framework components | `Trace` | Trace event type mapped (Critical→Fatal … Verbose→Debug); plain writes are Debug |
| `Console.Out` / `Console.Error` | Libraries and tools that print to the console | `Console` / `Console.Error` | Information / Warning |
| `EventSource` | Azure SDK, `System.Net.Http`, `System.Net.Security`, other modern libraries | `EventSource.<source name>` | EventSource level mapped; Warning and above by default |
| `Microsoft.Extensions.Logging` | Newer libraries that take an `ILogger` | the logger category | MEL level mapped |

**Serilog:** Microsoft.Extensions.Logging is already covered by `UseLogTierSerilog()`. Trace, Console and EventSource capture start as soon as `Enrich.WithLogTier(...)` (called by `UseLogTierSerilog`) builds the pipeline, and are written through `Log.Logger`. `UseLogTierSerilog` sets `Log.Logger`; if you build the logger yourself, assign it to `Log.Logger`.

**log4net:** capture starts when the LogTier layout is activated, from XML configuration or from `LogTierLog4NetConfigurator`. For libraries that use `Microsoft.Extensions.Logging` on .NET Framework, add the provider to their logger factory:

```csharp
loggerFactory.AddLogTierLog4Net();   // or: new LoggerFactory(new[] { new LogTierLog4NetLoggerProvider() })
```

**TraceSource:** named `TraceSource`s use their own listener lists. Add the LogTier listener as a shared listener so those sources are captured too:

```xml
<system.diagnostics>
  <sharedListeners>
    <add name="LogTier" type="LogTier.Log4Net.LogTierTraceListener, LogTier.Log4Net" />
  </sharedListeners>
  <sources>
    <source name="System.ServiceModel" switchValue="Warning">
      <listeners><add name="LogTier" /></listeners>
    </source>
  </sources>
</system.diagnostics>
```

(With Serilog use `LogTier.Serilog.LogTierTraceListener, LogTier.Serilog`.)

### 9.1 Turning captures off or tuning them

Serilog (`LogTierOptions`) and log4net (`LogTierLog4NetOptions`) share the same settings:

| Setting | Default | Meaning |
|---|---|---|
| `CaptureTrace` | `true` | Trace and TraceSource output |
| `CaptureConsole` | `true` | Console.Out and Console.Error lines |
| `CaptureEventSources` | `true` | EventSource events |
| `EventSourceMinimumLevel` | `Warning` | Lowest EventSource level captured (`Informational` and `Verbose` are very chatty) |
| `ExcludedEventSources` | runtime counters, TPL, Microsoft-Extensions-Logging and other sources that repeat what is already logged | Add names to leave out more sources |

log4net XML configuration sets them on the layout:

```xml
<layout type="LogTier.Log4Net.LogTierJsonLayout, LogTier.Log4Net">
  <CaptureConsole value="false" />
  <EventSourceMinimumLevel value="Informational" />
  <ExcludeEventSources value="Microsoft-ApplicationInsights-Core" />
</layout>
```

`LogTierCapture.Stop()` stops every capture and restores the console.

### 9.2 Console capture and console sinks

Console lines that already are LogTier JSON pass straight through, so `WriteTo.LogTierConsole(...)` and a log4net `ConsoleAppender` with the LogTier layout keep writing normally. A console sink or appender with a **different** format writes text that console capture turns into an event again, so each of those lines appears twice. Use the LogTier formatter/layout on console outputs, or set `CaptureConsole = false`. Writes made while a captured message is being logged go straight to the real console, so capture can never loop.

## 10. HTTP header safety

LogTier uses an **allow-list** model for HTTP headers.

Default captured headers are intentionally limited. Authentication/session secrets should never be copied into audit logs.

Sensitive header names such as the following are redacted if explicitly included:

- `Authorization`
- `Proxy-Authorization`
- `Cookie`
- `Set-Cookie`
- `X-Api-Key`

Do not add credentials, tokens, session secrets, passwords, private cryptographic material, or unnecessary sensitive payloads to structured logging properties.

Application-defined custom properties remain the application's responsibility.

## 11. Example output

A request event is emitted as one JSON object per line:

```json
{
  "timestamp": "2026-09-07T22:55:12.123Z",
  "m2131": {
    "memorandum": "M-21-31",
    "event_logging_tier": "EL1",
    "schema_version": "1.0.0"
  },
  "event": {
    "id": "2fca0a48-6c3b-414f-a07e-c7627f578aef",
    "type_id": "APP-HTTP-REQUEST-COMPLETED",
    "category": "ApplicationTransaction",
    "status_code": "Completed",
    "level": "Information",
    "message": "HTTP GET /api/cases/42 completed with status 200 in 18.245 ms"
  },
  "device": {
    "id": "api-node-001",
    "hostname": "APP01"
  },
  "session": {
    "transaction_id": "0HN8EXAMPLE",
    "trace_id": "...",
    "span_id": "..."
  },
  "network": {
    "source": {
      "ipv4": "10.10.1.20",
      "port": 51122
    },
    "destination": {
      "ipv4": "10.10.1.50",
      "port": 443
    }
  },
  "http": {
    "method": "GET",
    "path": "/api/cases/42",
    "status_code": 200,
    "response_time_ms": 18.245
  },
  "user": {
    "id": "user-123",
    "name": "DOMAIN\\jsmith"
  },
  "command": "GET /api/cases/42"
}
```

Fields that are not applicable to a particular event may be emitted as `null`. The library does not fabricate source data that the application or hosting environment did not provide.

## 12. Troubleshooting

### No log file is created

Verify:

1. the destination directory exists or the application identity can create it;
2. the process identity has write permission;
3. the configured minimum log level allows the event;
4. log4net was initialized when using XML configuration;
5. the LogTier formatter/layout is attached to the active appender/sink.

### ASP.NET 4.8 request fields are empty

Verify that `LogTierHttpModule` is registered under `system.webServer/modules` and that the request is processed through the managed pipeline.

### Client IP shows the proxy address

The logger can only record the network information visible to the application. Configure trusted proxy/forwarded-header processing at the hosting layer before relying on forwarded client information.

### User ID is empty

The user field is populated only when the hosting application exposes an authenticated principal/claim at the time the event is written. Confirm authentication middleware/module ordering and identity claims.

### HTTP headers are missing

Confirm:

- `IncludeHttpHeaders = true`;
- the header is included in the allow-list;
- the header actually exists on the request.

Sensitive headers are intentionally redacted.

## 13. Security and operational requirements

LogTier addresses **application log normalization**. Production deployments still need architecture and operations that address the applicable M-21-31 requirements, including:

- centralized collection/aggregation;
- near-real-time forwarding where required;
- authenticated and encrypted log transport;
- log integrity and unauthorized-modification protection;
- role-based access to logs;
- retention and archival requirements;
- monitoring/alerting when logging or forwarding fails;
- authoritative time synchronization;
- storage capacity planning;
- incident-response access and searchability;
- other required infrastructure/network/security telemetry.

Do not describe an entire system as "M-21-31 compliant" solely because it uses this library.

## 14. Baseline

`v1.0.0` established a neutral M-21-31/EL1 application logging contract with separate Serilog and log4net integrations. `v1.1.0` captures output other libraries write through Trace, Console, EventSource and (log4net) Microsoft.Extensions.Logging, on by default.

All package IDs, namespaces, types, middleware, extension methods, samples, documentation, and NuGet metadata use the `LogTier` naming. Only the JSON values that identify the standard (`m2131.memorandum = "M-21-31"`, `m2131.event_logging_tier = "EL1"`) keep the memorandum's name.

### Performance and safety notes

- The Serilog formatter streams JSON with `Utf8JsonWriter` into a reused per-thread buffer, and the log4net layout streams with `JsonTextWriter`; neither builds dictionaries per event.
- Values that never change (memorandum, tier, device, host, process, application) are created once; automatic event type IDs are cached (up to 10,000 distinct templates or loggers).
- The ASP.NET Core middleware pushes one request enricher instead of one `LogContext` property per field, and honours `IncludeNetworkEndpointData` for IP addresses as well as ports.
- The transaction ID comes from `X-Correlation-ID`, then `X-Request-ID`, then the W3C trace ID, in both packages.
- Exceptions include their inner exceptions (`exception.inner`, up to 8 levels).
- The log4net layout writes the exception inside the JSON object, so appenders never add stack-trace lines that would break JSONL, and it leaves out log4net's own `log4net:*` properties (host name and the Windows account of the process).
- log4net automatic event type IDs use logger and level only. Set `IncludeLocationInEventTypeId = true` (or `<IncludeLocationInEventTypeId value="true" />` on the layout) to add class and method; that walks the call stack on every event.

## 15. Tests

**Requirements to build:** Windows (the .NET Framework 4.8 package and its tests), .NET SDK 10.0.100 or a later feature
band (`global.json`), the .NET 6 and .NET 8 targeting packs for the multi-target package, NuGet access to restore
dependencies, and Visual Studio 2022 17.14+ / 2026 or the CLI. The .NET Framework projects reference
`Microsoft.NETFramework.ReferenceAssemblies.net48`, so SDK-based builds restore the 4.8 reference assemblies.

```cmd
dotnet build LogTier.slnx -c Release
dotnet test LogTier.slnx -c Release --no-build
```

`scripts\build-and-test.cmd` runs everything on Windows: the build (warnings as errors) and the .NET tests.
`scripts\pack.cmd` runs the same build and tests before it packs.

| Project | Command | Covers |
|---|---|---|
| `LogTier.Serilog.IntegrationTests` (.NET 10) | `dotnet test LogTier.slnx` | The whole Serilog pipeline into the LogTier JSON: M-21-31 / EL1 values, UTC millisecond timestamps, event and type ids, device, session, network fields, preserved properties, exception chains, HTTP request enrichment, and the captured Trace, Console and EventSource output. |
| `LogTier.Log4Net.IntegrationTests` (.NET Framework 4.8) | same | The same contract through log4net: context properties, exceptions, and the captured Trace, Console, EventSource and Microsoft.Extensions.Logging output. |

## 16. Production and deployment

### 16.1 Local NuGet feed

The packages are not published to NuGet.org. The ready-built 1.1.0 packages are kept in the repository's `nuget/` folder, which can be used directly as the local feed (for example `dotnet nuget add source "C:\path\to\LogTier\nuget" --name LogTierLocal`).

If you are testing locally before publishing the packages, create a folder such as:

```text
C:\LocalNuGet
```

Place the generated `.nupkg` files in that folder.

Add the source with the .NET CLI:

```bash
dotnet nuget add source "C:\LocalNuGet" --name LogTierLocal
```

Or add it in Visual Studio:

```text
Tools
  > NuGet Package Manager
    > Package Manager Settings
      > Package Sources
```

Then install normally:

```bash
dotnet add package LogTier.Serilog --version 1.1.0 --source "C:\LocalNuGet"
```

For a .NET Framework 4.8 project in Package Manager Console:

```powershell
Install-Package LogTier.Log4Net -Version 1.1.0 -Source "C:\LocalNuGet"
```

### 16.2 Publishing to a NuGet feed

Before publishing, validate the version and package metadata in both `.csproj` files.

Example for NuGet.org:

```bash
dotnet nuget push artifacts/LogTier.Serilog.1.1.0.nupkg \
  --api-key YOUR_NUGET_API_KEY \
  --source https://api.nuget.org/v3/index.json

dotnet nuget push artifacts/LogTier.Log4Net.1.1.0.nupkg \
  --api-key YOUR_NUGET_API_KEY \
  --source https://api.nuget.org/v3/index.json
```

For Azure Artifacts, GitHub Packages, Artifactory, Nexus, or another enterprise feed, use that feed's authenticated NuGet v3 source and credential mechanism.

Never commit feed API keys or credentials to the repository.

### 16.3 Pack both NuGet packages

```cmd
scripts\pack.cmd
```

or, in PowerShell, `scripts\pack.ps1`. Both build, run the tests and pack. Expected outputs:

```text
artifacts/LogTier.Serilog.1.1.0.nupkg
artifacts/LogTier.Serilog.1.1.0.snupkg
artifacts/LogTier.Log4Net.1.1.0.nupkg
artifacts/LogTier.Log4Net.1.1.0.snupkg
```

The scripts also replace the contents of `nuget/` with the new packages, so that folder always holds the current
version and can be used as a local feed.
