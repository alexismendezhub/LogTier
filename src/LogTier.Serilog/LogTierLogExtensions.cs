using Serilog;

namespace LogTier.Serilog;

/// <summary>
/// Helpers for common application events explicitly represented in M-21-31 Appendix C.
/// </summary>
public static class LogTierLogExtensions
{
    /// <summary>Writes the APP-STARTED event.</summary>
    public static void LogTierApplicationStarted(this ILogger logger)
        => ForStandardEvent(logger, LogTierEventTypes.ApplicationStarted, LogTierEventCategories.ApplicationOperation, "Success")
            .Information("Application started");

    /// <summary>Writes the APP-STOPPED event.</summary>
    public static void LogTierApplicationStopped(this ILogger logger)
        => ForStandardEvent(logger, LogTierEventTypes.ApplicationStopped, LogTierEventCategories.ApplicationOperation, "Success")
            .Information("Application stopped");

    /// <summary>Writes the APP-FAILURE event for a failed operation.</summary>
    public static void LogTierApplicationFailure(this ILogger logger, Exception exception, string operation)
        => ForStandardEvent(logger, LogTierEventTypes.ApplicationFailure, LogTierEventCategories.ApplicationFailure, "Failure")
            .ForContext(LogTierProperties.Command, operation)
            .Error(exception, "Application operation failed: {Operation}", operation);

    /// <summary>Writes APP-AUTH-SUCCESS or APP-AUTH-FAILURE for an authentication attempt.</summary>
    public static void LogTierAuthenticationAttempt(
        this ILogger logger,
        bool succeeded,
        string? userId = null,
        string? userName = null)
    {
        var eventLogger = ForStandardEvent(
                logger,
                succeeded ? LogTierEventTypes.AuthenticationSucceeded : LogTierEventTypes.AuthenticationFailed,
                LogTierEventCategories.ApplicationAuthentication,
                succeeded ? "Success" : "Failure")
            .ForContext(LogTierProperties.UserId, userId)
            .ForContext(LogTierProperties.UserName, userName);

        if (succeeded)
        {
            eventLogger.Information("Application authentication succeeded for user {UserId}", userId ?? userName ?? "unknown");
        }
        else
        {
            eventLogger.Warning("Application authentication failed for user {UserId}", userId ?? userName ?? "unknown");
        }
    }

    /// <summary>Writes the APP-ACCOUNT-CHANGED event.</summary>
    public static void LogTierAccountChanged(
        this ILogger logger,
        string changeType,
        string affectedUserId,
        string? actingUserId = null)
        => ForStandardEvent(logger, LogTierEventTypes.AccountChanged, LogTierEventCategories.ApplicationAccount, "Success")
            .ForContext("AccountChangeType", changeType)
            .ForContext("AffectedUserId", affectedUserId)
            .ForContext(LogTierProperties.UserId, actingUserId)
            .Information("Application account changed: {AccountChangeType} for {AffectedUserId}", changeType, affectedUserId);

    /// <summary>Writes the APP-PRIVILEGE-USED event.</summary>
    public static void LogTierPrivilegeUsed(
        this ILogger logger,
        string privilege,
        string? userId = null,
        bool succeeded = true)
        => ForStandardEvent(
                logger,
                LogTierEventTypes.PrivilegeUsed,
                LogTierEventCategories.ApplicationPrivilege,
                succeeded ? "Success" : "Failure")
            .ForContext("Privilege", privilege)
            .ForContext(LogTierProperties.UserId, userId)
            .Information("Application privilege {Privilege} used; success={Succeeded}", privilege, succeeded);

    /// <summary>Writes the APP-CONFIG-CHANGED event.</summary>
    public static void LogTierConfigurationChanged(
        this ILogger logger,
        string setting,
        string? actingUserId = null)
        => ForStandardEvent(logger, LogTierEventTypes.ConfigurationChanged, LogTierEventCategories.ApplicationConfiguration, "Success")
            .ForContext("ConfigurationSetting", setting)
            .ForContext(LogTierProperties.UserId, actingUserId)
            .Information("Application configuration changed: {ConfigurationSetting}", setting);

    /// <summary>Writes the APP-TRANSACTION event.</summary>
    public static void LogTierTransaction(
        this ILogger logger,
        string transactionType,
        string transactionId,
        string statusCode,
        string? userId = null)
        => ForStandardEvent(logger, LogTierEventTypes.ApplicationTransaction, LogTierEventCategories.ApplicationTransaction, statusCode)
            .ForContext("TransactionType", transactionType)
            .ForContext(LogTierProperties.TransactionId, transactionId)
            .ForContext(LogTierProperties.UserId, userId)
            .Information("Application transaction {TransactionType} completed with status {StatusCode}", transactionType, statusCode);

    private static ILogger ForStandardEvent(ILogger logger, string eventTypeId, string category, string statusCode)
    {
        ArgumentNullException.ThrowIfNull(logger);
        return logger
            .ForContext(LogTierProperties.EventTypeId, eventTypeId)
            .ForContext(LogTierProperties.EventCategory, category)
            .ForContext(LogTierProperties.EventStatusCode, statusCode);
    }
}
