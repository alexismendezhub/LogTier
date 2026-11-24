# LogTier

`LogTier` is a reusable logging library family for applications that need a consistent **OMB M-21-31 Event Logging Tier EL1-aligned structured logging format**.

It does **not** fork or modify Serilog or log4net. Instead, it integrates with those logging frameworks and normalizes their events into the same newline-delimited JSON (`JSONL`) contract.

It has two packages:

- **`LogTier.Serilog`** for .NET 6, .NET 8 and .NET 10 applications that log with Serilog or `ILogger<T>`.
- **`LogTier.Log4Net`** for .NET Framework 4.8 applications that log with log4net.

Every event is written with the same fields: time in UTC, event type and status, host, session, correlation and
trace ids, the HTTP request, the user, the application and the exception.

## How to use it

### Install the package

The packages are not published to NuGet.org. The built packages are in the `nuget/` folder of this repository, which
works as a local feed:

```cmd
dotnet nuget add source "C:\path\to\LogTier\nuget" --name LogTier
dotnet add package LogTier.Serilog --version 1.1.0
```

In a .NET Framework 4.8 project, from the Package Manager Console:

```powershell
Install-Package LogTier.Log4Net -Version 1.1.0
```

### Initialize it with Serilog (.NET 6, 8 and 10)

In `Program.cs`:

```csharp
using LogTier.Serilog;
using Serilog;

var builder = WebApplication.CreateBuilder(args);

var logTier = new LogTierOptions
{
    ApplicationName = "Orders.Api",
    ApplicationVersion = "1.0.0",
    EnvironmentName = builder.Environment.EnvironmentName
};

builder.Host.UseLogTierSerilog(
    logTier,
    (_, logger) => logger
        .MinimumLevel.Information()
        .WriteTo.LogTierConsole(logTier)
        .WriteTo.LogTierFile(logTier, path: "logs/logtier-.jsonl", retainedFileCountLimit: 30));

var app = builder.Build();

// Behind a trusted proxy or load balancer, configure forwarded headers before this line.
app.UseLogTierRequestLogging(logTier);

Log.Logger.LogTierApplicationStarted();

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

After that the application logs as usual with `ILogger<T>`; every event comes out in the LogTier format.

### Initialize it with log4net (.NET Framework 4.8)

Use the LogTier layout in the appender of `web.config` or `app.config`:

```xml
<configuration>
  <configSections>
    <section name="log4net" type="log4net.Config.Log4NetConfigurationSectionHandler, log4net" />
  </configSections>

  <log4net>
    <appender name="LogTierFile" type="log4net.Appender.RollingFileAppender">
      <file value="C:\Logs\legacy-logtier.jsonl" />
      <appendToFile value="true" />
      <layout type="LogTier.Log4Net.LogTierJsonLayout, LogTier.Log4Net">
        <ApplicationName value="Legacy.CaseManagement" />
        <ApplicationVersion value="12.4.0" />
        <EnvironmentName value="Production" />
      </layout>
    </appender>
    <root>
      <level value="INFO" />
      <appender-ref ref="LogTierFile" />
    </root>
  </log4net>
</configuration>
```

and start log4net once, for example in `Application_Start` of `Global.asax.cs`, if the application does not do it
already:

```csharp
log4net.Config.XmlConfigurator.Configure();
```

The `log.Info(...)`, `log.Warn(...)` and `log.Error(...)` calls the application already has come out in the LogTier
format.

## How to run it

It is a library, so there is nothing to start. To build it and try the samples:

**Requirements:** Windows, the .NET SDK 10 and the .NET 6 and .NET 8 targeting packs.

Open `LogTier.slnx` in Visual Studio, or:

```cmd
dotnet build LogTier.slnx -c Release
```

`samples\LogTier.Serilog.Sample` and `samples\LogTier.Log4Net.Sample` show each package set up in an application.

## Configuration

Three values identify the application in every event: `ApplicationName`, `ApplicationVersion` and
`EnvironmentName`. With Serilog they are set in `LogTierOptions`; with log4net, in the layout of the appender. The
examples above show both.

## Tests

```cmd
scripts\build-and-test.cmd
```

It runs the build (warnings as errors) and the .NET tests. `scripts\pack.cmd` runs the same build and tests before
it packs the packages.

## License and disclaimer

Licensed under the [Apache License 2.0](LICENSE): anyone may use, modify and distribute it, including commercially.

It is provided as an example, "as is", without warranty of any kind. Review and test it before using it in
production; the authors are not liable for any damage that results from its use.
