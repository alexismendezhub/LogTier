using System;
using System.Collections.Generic;
using log4net;

namespace LogTier.Log4Net
{
    /// <summary>Convenience methods for common application events identified by M-21-31 Appendix C.</summary>
    public static class LogTierLogExtensions
    {
        /// <summary>Writes the APP-STARTED event.</summary>
        public static void LogTierApplicationStarted(this ILog logger)
        {
            Write(logger, LogTierEventTypes.ApplicationStarted, LogTierEventCategories.ApplicationOperation, "Success", null, null, "Application started");
        }

        /// <summary>Writes the APP-STOPPED event.</summary>
        public static void LogTierApplicationStopped(this ILog logger)
        {
            Write(logger, LogTierEventTypes.ApplicationStopped, LogTierEventCategories.ApplicationOperation, "Success", null, null, "Application stopped");
        }

        /// <summary>Writes the APP-FAILURE event for a failed operation.</summary>
        public static void LogTierApplicationFailure(this ILog logger, Exception exception, string operation)
        {
            var properties = new Dictionary<string, object> { [LogTierProperties.Command] = operation };
            Write(logger, LogTierEventTypes.ApplicationFailure, LogTierEventCategories.ApplicationFailure, "Failure", properties, exception, "Application operation failed: " + operation);
        }

        /// <summary>Writes APP-AUTH-SUCCESS or APP-AUTH-FAILURE for an authentication attempt.</summary>
        public static void LogTierAuthenticationAttempt(this ILog logger, bool succeeded, string userId = null, string userName = null)
        {
            var properties = new Dictionary<string, object>
            {
                [LogTierProperties.UserId] = userId,
                [LogTierProperties.UserName] = userName
            };
            Write(logger,
                succeeded ? LogTierEventTypes.AuthenticationSucceeded : LogTierEventTypes.AuthenticationFailed,
                LogTierEventCategories.ApplicationAuthentication,
                succeeded ? "Success" : "Failure",
                properties,
                null,
                "Application authentication " + (succeeded ? "succeeded" : "failed") + " for user " + (userId ?? userName ?? "unknown"),
                succeeded ? LogTierLogLevel.Info : LogTierLogLevel.Warn);
        }

        /// <summary>Writes the APP-ACCOUNT-CHANGED event.</summary>
        public static void LogTierAccountChanged(this ILog logger, string changeType, string affectedUserId, string actingUserId = null)
        {
            var properties = new Dictionary<string, object>
            {
                ["AccountChangeType"] = changeType,
                ["AffectedUserId"] = affectedUserId,
                [LogTierProperties.UserId] = actingUserId
            };
            Write(logger, LogTierEventTypes.AccountChanged, LogTierEventCategories.ApplicationAccount, "Success", properties, null,
                "Application account changed: " + changeType + " for " + affectedUserId);
        }

        /// <summary>Writes the APP-PRIVILEGE-USED event.</summary>
        public static void LogTierPrivilegeUsed(this ILog logger, string privilege, string userId = null, bool succeeded = true)
        {
            var properties = new Dictionary<string, object>
            {
                ["Privilege"] = privilege,
                [LogTierProperties.UserId] = userId
            };
            Write(logger, LogTierEventTypes.PrivilegeUsed, LogTierEventCategories.ApplicationPrivilege,
                succeeded ? "Success" : "Failure", properties, null,
                "Application privilege " + privilege + " used; success=" + succeeded);
        }

        /// <summary>Writes the APP-CONFIG-CHANGED event.</summary>
        public static void LogTierConfigurationChanged(this ILog logger, string setting, string actingUserId = null)
        {
            var properties = new Dictionary<string, object>
            {
                ["ConfigurationSetting"] = setting,
                [LogTierProperties.UserId] = actingUserId
            };
            Write(logger, LogTierEventTypes.ConfigurationChanged, LogTierEventCategories.ApplicationConfiguration,
                "Success", properties, null, "Application configuration changed: " + setting);
        }

        /// <summary>Writes the APP-TRANSACTION event.</summary>
        public static void LogTierTransaction(this ILog logger, string transactionType, string transactionId, string statusCode, string userId = null)
        {
            var properties = new Dictionary<string, object>
            {
                ["TransactionType"] = transactionType,
                [LogTierProperties.TransactionId] = transactionId,
                [LogTierProperties.UserId] = userId
            };
            Write(logger, LogTierEventTypes.ApplicationTransaction, LogTierEventCategories.ApplicationTransaction,
                statusCode, properties, null, "Application transaction " + transactionType + " completed with status " + statusCode);
        }

        /// <summary>Writes an information event with an application-defined stable event type ID.</summary>
        public static void LogTierInfo(this ILog logger, string eventTypeId, string category, string statusCode, string message, IDictionary<string, object> properties = null)
        {
            Write(logger, eventTypeId, category, statusCode, properties, null, message);
        }

        /// <summary>Writes an error event with an application-defined stable event type ID.</summary>
        public static void LogTierError(this ILog logger, string eventTypeId, string category, string statusCode, string message, Exception exception, IDictionary<string, object> properties = null)
        {
            Write(logger, eventTypeId, category, statusCode, properties, exception, message, LogTierLogLevel.Error);
        }

        private static void Write(ILog logger, string eventTypeId, string category, string statusCode,
            IDictionary<string, object> properties, Exception exception, string message, LogTierLogLevel level = LogTierLogLevel.Info)
        {
            if (logger == null) throw new ArgumentNullException(nameof(logger));
            var all = properties != null
                ? new Dictionary<string, object>(properties, StringComparer.Ordinal)
                : new Dictionary<string, object>(StringComparer.Ordinal);
            all[LogTierProperties.EventTypeId] = eventTypeId;
            all[LogTierProperties.EventCategory] = category;
            all[LogTierProperties.EventStatusCode] = statusCode;
            all[LogTierProperties.EventId] = Guid.NewGuid().ToString("D");
            all[LogTierProperties.Memorandum] = LogTierStandard.Memorandum;
            all[LogTierProperties.EventLoggingTier] = LogTierStandard.EventLoggingTier;

            using (LogTierContext.Push(all))
            {
                if (level == LogTierLogLevel.Error) logger.Error(message, exception);
                else if (level == LogTierLogLevel.Warn) logger.Warn(message);
                else logger.Info(message);
            }
        }

        private enum LogTierLogLevel { Info, Warn, Error }
    }
}
