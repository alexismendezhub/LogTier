using System;
using System.Collections.Generic;
using log4net.Core;
using MelLogger = Microsoft.Extensions.Logging.ILogger;
using Microsoft.Extensions.Logging;
using MelLogLevel = Microsoft.Extensions.Logging.LogLevel;

namespace LogTier.Log4Net
{
    /// <summary>
    /// Microsoft.Extensions.Logging provider that sends ILogger output (used by newer libraries such as the Azure SDK)
    /// to log4net, so it is written in the LogTier JSON format.
    /// </summary>
    public sealed class LogTierLog4NetLoggerProvider : ILoggerProvider
    {
        /// <inheritdoc />
        public MelLogger CreateLogger(string categoryName) => new Log4NetLogger(categoryName ?? "Microsoft.Extensions.Logging");

        /// <inheritdoc />
        public void Dispose() { }

        private sealed class Log4NetLogger : MelLogger
        {
            private readonly log4net.Core.ILogger _logger;

            public Log4NetLogger(string categoryName)
            {
                _logger = log4net.LogManager.GetLogger(categoryName).Logger;
            }

            public IDisposable BeginScope<TState>(TState state)
            {
                var pairs = state as IEnumerable<KeyValuePair<string, object>>;
                if (pairs == null) return NoopScope.Instance;

                var properties = new Dictionary<string, object>(StringComparer.Ordinal);
                foreach (var pair in pairs)
                {
                    if (pair.Key != "{OriginalFormat}") properties[pair.Key] = pair.Value;
                }
                return LogTierContext.Push(properties);
            }

            public bool IsEnabled(MelLogLevel logLevel) => logLevel != MelLogLevel.None && _logger.IsEnabledFor(Map(logLevel));

            public void Log<TState>(MelLogLevel logLevel, EventId eventId, TState state, Exception exception, Func<TState, Exception, string> formatter)
            {
                if (!IsEnabled(logLevel)) return;

                var properties = new Dictionary<string, object>(StringComparer.Ordinal) { ["CaptureSource"] = "MicrosoftExtensionsLogging" };
                if (eventId.Id != 0) properties["EventId"] = eventId.Id;
                if (!string.IsNullOrEmpty(eventId.Name)) properties["EventName"] = eventId.Name;
                var pairs = state as IEnumerable<KeyValuePair<string, object>>;
                if (pairs != null)
                {
                    foreach (var pair in pairs)
                    {
                        if (pair.Key != "{OriginalFormat}") properties[pair.Key] = pair.Value;
                    }
                }

                var message = formatter != null ? formatter(state, exception) : Convert.ToString(state);
                using (LogTierContext.Push(properties))
                {
                    _logger.Log(typeof(Log4NetLogger), Map(logLevel), message, exception);
                }
            }

            private static Level Map(MelLogLevel logLevel)
            {
                switch (logLevel)
                {
                    case MelLogLevel.Trace: return Level.Trace;
                    case MelLogLevel.Debug: return Level.Debug;
                    case MelLogLevel.Information: return Level.Info;
                    case MelLogLevel.Warning: return Level.Warn;
                    case MelLogLevel.Error: return Level.Error;
                    default: return Level.Fatal;
                }
            }
        }

        private sealed class NoopScope : IDisposable
        {
            public static readonly NoopScope Instance = new NoopScope();
            public void Dispose() { }
        }
    }

    /// <summary>Registration helpers for <see cref="LogTierLog4NetLoggerProvider"/>.</summary>
    public static class LogTierLoggerFactoryExtensions
    {
        /// <summary>Adds the LogTier log4net provider to an existing logger factory.</summary>
        public static ILoggerFactory AddLogTierLog4Net(this ILoggerFactory factory)
        {
            if (factory == null) throw new ArgumentNullException(nameof(factory));
            factory.AddProvider(new LogTierLog4NetLoggerProvider());
            return factory;
        }
    }
}
