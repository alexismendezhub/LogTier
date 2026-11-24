using Microsoft.Extensions.Hosting;
using Serilog;
using Serilog.Configuration;
using Serilog.Events;

namespace LogTier.Serilog;

/// <summary>Serilog configuration helpers that add the LogTier enricher, formatter and sinks.</summary>
public static class LogTierConfigurationExtensions
{
    /// <summary>Adds M-21-31 metadata to every Serilog event.</summary>
    public static LoggerConfiguration WithLogTier(
        this LoggerEnrichmentConfiguration enrichmentConfiguration,
        LogTierOptions options)
    {
        ArgumentNullException.ThrowIfNull(enrichmentConfiguration);
        ArgumentNullException.ThrowIfNull(options);
        return enrichmentConfiguration.With(new LogTierLogEventEnricher(options));
    }

    /// <summary>Writes newline-delimited M-21-31 JSON to the console.</summary>
    public static LoggerConfiguration LogTierConsole(
        this LoggerSinkConfiguration sinkConfiguration,
        LogTierOptions options,
        LogEventLevel restrictedToMinimumLevel = LogEventLevel.Verbose)
    {
        ArgumentNullException.ThrowIfNull(sinkConfiguration);
        ArgumentNullException.ThrowIfNull(options);
        return sinkConfiguration.Console(new LogTierJsonFormatter(options), restrictedToMinimumLevel);
    }

    /// <summary>Writes newline-delimited M-21-31 JSON to rolling files.</summary>
    public static LoggerConfiguration LogTierFile(
        this LoggerSinkConfiguration sinkConfiguration,
        LogTierOptions options,
        string path,
        RollingInterval rollingInterval = RollingInterval.Day,
        int? retainedFileCountLimit = null,
        LogEventLevel restrictedToMinimumLevel = LogEventLevel.Verbose,
        bool shared = false)
    {
        ArgumentNullException.ThrowIfNull(sinkConfiguration);
        ArgumentNullException.ThrowIfNull(options);
        if (string.IsNullOrWhiteSpace(path))
        {
            throw new ArgumentException("Path is required.", nameof(path));
        }

        return sinkConfiguration.File(
            formatter: new LogTierJsonFormatter(options),
            path: path,
            restrictedToMinimumLevel: restrictedToMinimumLevel,
            rollingInterval: rollingInterval,
            retainedFileCountLimit: retainedFileCountLimit,
            shared: shared,
            rollOnFileSizeLimit: true);
    }

    /// <summary>
    /// Configures Serilog as the Microsoft.Extensions.Logging provider and applies the M-21-31 enricher.
    /// If no callback is supplied, the package writes M-21-31 JSON to the console.
    /// </summary>
    public static IHostBuilder UseLogTierSerilog(
        this IHostBuilder hostBuilder,
        LogTierOptions options,
        Action<HostBuilderContext, LoggerConfiguration>? configure = null,
        bool preserveStaticLogger = false)
    {
        ArgumentNullException.ThrowIfNull(hostBuilder);
        ArgumentNullException.ThrowIfNull(options);

        return hostBuilder.UseSerilog(
            (context, _, loggerConfiguration) =>
            {
                loggerConfiguration
                    .Enrich.FromLogContext()
                    .Enrich.WithLogTier(options);

                if (configure is null)
                {
                    loggerConfiguration.WriteTo.LogTierConsole(options);
                }
                else
                {
                    configure(context, loggerConfiguration);
                }
            },
            preserveStaticLogger: preserveStaticLogger);
    }
}
