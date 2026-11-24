namespace LogTier.Serilog;

/// <summary>
/// Identifies the federal standard the schema follows: OMB memorandum M-21-31, Event Logging tier EL1 (Basic).
/// </summary>
public static class LogTierStandard
{
    /// <summary>OMB memorandum identifier.</summary>
    public const string Memorandum = "M-21-31";
    /// <summary>Event logging maturity tier.</summary>
    public const string EventLoggingTier = "EL1";
}

/// <summary>
/// Well-known property names emitted by the package.
/// </summary>
public static class LogTierProperties
{
    /// <summary>Log property that carries the memorandum.</summary>
    public const string Memorandum = "LogTierMemorandum";
    /// <summary>Log property that carries the event logging tier.</summary>
    public const string EventLoggingTier = "LogTierEventLoggingTier";
    /// <summary>Log property that carries the schema version.</summary>
    public const string SchemaVersion = "LogTierSchemaVersion";
    /// <summary>Log property that carries the event id.</summary>
    public const string EventId = "LogTierEventId";
    /// <summary>Log property that carries the event type id.</summary>
    public const string EventTypeId = "LogTierEventTypeId";
    /// <summary>Log property that carries the event category.</summary>
    public const string EventCategory = "LogTierEventCategory";
    /// <summary>Log property that carries the event status code.</summary>
    public const string EventStatusCode = "LogTierEventStatusCode";
    /// <summary>Log property that carries the device id.</summary>
    public const string DeviceId = "LogTierDeviceId";
    /// <summary>Log property that carries the device mac.</summary>
    public const string DeviceMac = "LogTierDeviceMac";
    /// <summary>Log property that carries the session id.</summary>
    public const string SessionId = "LogTierSessionId";
    /// <summary>Log property that carries the transaction id.</summary>
    public const string TransactionId = "LogTierTransactionId";
    /// <summary>Log property that carries the autonomous system number.</summary>
    public const string AutonomousSystemNumber = "LogTierAutonomousSystemNumber";
    /// <summary>Log property that carries the source ipv4.</summary>
    public const string SourceIpv4 = "LogTierSourceIpv4";
    /// <summary>Log property that carries the source ipv6.</summary>
    public const string SourceIpv6 = "LogTierSourceIpv6";
    /// <summary>Log property that carries the source port.</summary>
    public const string SourcePort = "LogTierSourcePort";
    /// <summary>Log property that carries the destination ipv4.</summary>
    public const string DestinationIpv4 = "LogTierDestinationIpv4";
    /// <summary>Log property that carries the destination ipv6.</summary>
    public const string DestinationIpv6 = "LogTierDestinationIpv6";
    /// <summary>Log property that carries the destination port.</summary>
    public const string DestinationPort = "LogTierDestinationPort";
    /// <summary>Log property that carries the response time ms.</summary>
    public const string ResponseTimeMs = "LogTierResponseTimeMs";
    /// <summary>Log property that carries the http headers.</summary>
    public const string HttpHeaders = "LogTierHttpHeaders";
    /// <summary>Log property that carries the http method.</summary>
    public const string HttpMethod = "LogTierHttpMethod";
    /// <summary>Log property that carries the http scheme.</summary>
    public const string HttpScheme = "LogTierHttpScheme";
    /// <summary>Log property that carries the http host.</summary>
    public const string HttpHost = "LogTierHttpHost";
    /// <summary>Log property that carries the http path.</summary>
    public const string HttpPath = "LogTierHttpPath";
    /// <summary>Log property that carries the http status code.</summary>
    public const string HttpStatusCode = "LogTierHttpStatusCode";
    /// <summary>Log property that carries the user name.</summary>
    public const string UserName = "LogTierUserName";
    /// <summary>Log property that carries the user id.</summary>
    public const string UserId = "LogTierUserId";
    /// <summary>Log property that carries the command.</summary>
    public const string Command = "LogTierCommand";
    /// <summary>Log property that carries the trace id.</summary>
    public const string TraceId = "LogTierTraceId";
    /// <summary>Log property that carries the span id.</summary>
    public const string SpanId = "LogTierSpanId";
    /// <summary>Log property that carries the application name.</summary>
    public const string ApplicationName = "LogTierApplicationName";
    /// <summary>Log property that carries the application version.</summary>
    public const string ApplicationVersion = "LogTierApplicationVersion";
    /// <summary>Log property that carries the environment name.</summary>
    public const string EnvironmentName = "LogTierEnvironmentName";
    /// <summary>Log property that carries the host name.</summary>
    public const string HostName = "LogTierHostName";
    /// <summary>Log property that carries the process id.</summary>
    public const string ProcessId = "LogTierProcessId";
    /// <summary>Log property that carries the thread id.</summary>
    public const string ThreadId = "LogTierThreadId";
}

/// <summary>
/// Standard event type identifiers for common application events called out by M-21-31 Appendix C.
/// Applications can define additional stable identifiers for domain-specific events.
/// </summary>
public static class LogTierEventTypes
{
    /// <summary>Event type ID for application started.</summary>
    public const string ApplicationStarted = "APP-STARTED";
    /// <summary>Event type ID for application stopped.</summary>
    public const string ApplicationStopped = "APP-STOPPED";
    /// <summary>Event type ID for application failure.</summary>
    public const string ApplicationFailure = "APP-FAILURE";
    /// <summary>Event type ID for configuration changed.</summary>
    public const string ConfigurationChanged = "APP-CONFIG-CHANGED";
    /// <summary>Event type ID for http request completed.</summary>
    public const string HttpRequestCompleted = "APP-HTTP-REQUEST-COMPLETED";
    /// <summary>Event type ID for authentication succeeded.</summary>
    public const string AuthenticationSucceeded = "APP-AUTH-SUCCESS";
    /// <summary>Event type ID for authentication failed.</summary>
    public const string AuthenticationFailed = "APP-AUTH-FAILURE";
    /// <summary>Event type ID for account changed.</summary>
    public const string AccountChanged = "APP-ACCOUNT-CHANGED";
    /// <summary>Event type ID for privilege used.</summary>
    public const string PrivilegeUsed = "APP-PRIVILEGE-USED";
    /// <summary>Event type ID for application transaction.</summary>
    public const string ApplicationTransaction = "APP-TRANSACTION";
}

/// <summary>Event category for log tier event categories events.</summary>
public static class LogTierEventCategories
{
    /// <summary>Event category for application operation events.</summary>
    public const string ApplicationOperation = "ApplicationOperation";
    /// <summary>Event category for application authentication events.</summary>
    public const string ApplicationAuthentication = "ApplicationAuthentication";
    /// <summary>Event category for application account events.</summary>
    public const string ApplicationAccount = "ApplicationAccount";
    /// <summary>Event category for application privilege events.</summary>
    public const string ApplicationPrivilege = "ApplicationPrivilege";
    /// <summary>Event category for application transaction events.</summary>
    public const string ApplicationTransaction = "ApplicationTransaction";
    /// <summary>Event category for application configuration events.</summary>
    public const string ApplicationConfiguration = "ApplicationConfiguration";
    /// <summary>Event category for application failure events.</summary>
    public const string ApplicationFailure = "ApplicationFailure";
}
