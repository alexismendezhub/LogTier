using System;
using LogTier.Log4Net;

internal static class Program
{
    private static void Main()
    {
        var log = LogTierLog4NetConfigurator.ConfigureRollingFile(new LogTierLog4NetOptions
        {
            ApplicationName = "LogTier.Log4Net.Sample",
            ApplicationVersion = "1.0.0",
            EnvironmentName = "Development",
            DeviceIdentifier = Environment.MachineName,
            LogFilePath = @"logs\logtier.jsonl"
        });

        log.LogTierApplicationStarted();
        log.Info("Ordinary legacy log4net event");
        log.LogTierAuthenticationAttempt(true, "demo-user", "demo-user");
        log.LogTierTransaction("DemoTransaction", "txn-demo-001", "Success", "demo-user");
        log.LogTierApplicationStopped();
    }
}
