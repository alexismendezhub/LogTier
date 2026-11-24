using System;
using System.Text;
using log4net;
using log4net.Appender;
using log4net.Repository.Hierarchy;

namespace LogTier.Log4Net
{
    /// <summary>Programmatic log4net configuration that installs the M-21-31 JSON layout.</summary>
    public static class LogTierLog4NetConfigurator
    {
        /// <summary>Installs a rolling-file appender that writes LogTier JSON and returns a logger.</summary>
        public static ILog ConfigureRollingFile(LogTierLog4NetOptions options, string loggerName = null)
        {
            if (options == null) throw new ArgumentNullException(nameof(options));

            var repository = LogManager.GetRepository() as Hierarchy;
            if (repository == null) throw new InvalidOperationException("The active log4net repository is not a Hierarchy repository.");

            var layout = new LogTierJsonLayout(options);
            var appender = new RollingFileAppender
            {
                Name = "LogTierRollingFile",
                File = options.LogFilePath,
                AppendToFile = true,
                RollingStyle = RollingFileAppender.RollingMode.Composite,
                DatePattern = ".yyyy-MM-dd",
                MaximumFileSize = options.MaximumFileSize,
                MaxSizeRollBackups = options.MaxSizeRollBackups,
                StaticLogFileName = true,
                Encoding = new UTF8Encoding(false),
                LockingModel = new FileAppender.MinimalLock(),
                Layout = layout
            };
            layout.ActivateOptions();
            appender.ActivateOptions();

            if (options.ReplaceExistingAppenders) repository.Root.RemoveAllAppenders();
            repository.Root.AddAppender(appender);
            repository.Configured = true;

            return LogManager.GetLogger(loggerName ?? options.ApplicationName ?? "LogTier");
        }
    }
}
