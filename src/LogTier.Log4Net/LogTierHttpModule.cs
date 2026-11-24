using System;
using System.Collections.Generic;
using System.Collections.Specialized;
using System.Configuration;
using System.Diagnostics;
using System.Globalization;
using System.Net;
using System.Net.Sockets;
using System.Security.Claims;
using System.Web;
using log4net;

namespace LogTier.Log4Net
{
    /// <summary>
    /// ASP.NET (System.Web) HTTP module that enriches all log4net events in a request and emits one completion event.
    /// Register it in web.config under system.webServer/modules.
    /// </summary>
    public sealed class LogTierHttpModule : IHttpModule
    {
        private const string StopwatchItemKey = "__LogTier_Stopwatch";
        private static readonly ILog Logger = LogManager.GetLogger(typeof(LogTierHttpModule));
        private static readonly LogTierLog4NetOptions Options = LoadOptions();

        /// <inheritdoc />
        public void Init(HttpApplication context)
        {
            if (context == null) throw new ArgumentNullException(nameof(context));
            context.BeginRequest += OnBeginRequest;
            context.PostAuthenticateRequest += OnPostAuthenticateRequest;
            context.EndRequest += OnEndRequest;
        }

        /// <inheritdoc />
        public void Dispose() { }

        private static void OnBeginRequest(object sender, EventArgs e)
        {
            var application = (HttpApplication)sender;
            var context = application.Context;
            var request = context.Request;
            context.Items[StopwatchItemKey] = Stopwatch.StartNew();

            LogTierContext.ClearRequestProperties();
            LogTierContext.Set(LogTierProperties.TransactionId, ResolveTransactionId(request));
            LogTierContext.Set(LogTierProperties.SessionId, Header(request, "X-Session-ID"));
            LogTierContext.Set(LogTierProperties.HttpMethod, request.HttpMethod);
            LogTierContext.Set(LogTierProperties.HttpScheme, request.Url != null ? request.Url.Scheme : null);
            LogTierContext.Set(LogTierProperties.HttpHost, request.Url != null ? request.Url.Authority : request.Headers["Host"]);
            LogTierContext.Set(LogTierProperties.HttpPath, request.Url != null ? request.Url.AbsolutePath : request.Path);
            LogTierContext.Set(LogTierProperties.Command, request.HttpMethod + " " + (request.Url != null ? request.Url.AbsolutePath : request.Path));

            if (Options.IncludeHttpHeaders)
                LogTierContext.Set(LogTierProperties.HttpHeaders, CaptureHeaders(request.Headers));

            if (Options.IncludeNetworkEndpointData)
            {
                var source = SplitIp(request.UserHostAddress);
                var destination = SplitIp(request.ServerVariables["LOCAL_ADDR"]);
                LogTierContext.Set(LogTierProperties.SourceIpv4, source.Item1);
                LogTierContext.Set(LogTierProperties.SourceIpv6, source.Item2);
                LogTierContext.Set(LogTierProperties.SourcePort, ParseInt(request.ServerVariables["REMOTE_PORT"]));
                LogTierContext.Set(LogTierProperties.DestinationIpv4, destination.Item1);
                LogTierContext.Set(LogTierProperties.DestinationIpv6, destination.Item2);
                LogTierContext.Set(LogTierProperties.DestinationPort, ParseInt(request.ServerVariables["SERVER_PORT"]));
            }
        }

        private static void OnPostAuthenticateRequest(object sender, EventArgs e)
        {
            var context = ((HttpApplication)sender).Context;
            var principal = context.User as ClaimsPrincipal;
            var userId = principal != null ? principal.FindFirst(ClaimTypes.NameIdentifier)?.Value : null;
            var userName = context.User != null && context.User.Identity != null ? context.User.Identity.Name : null;
            LogTierContext.Set(LogTierProperties.UserId, userId);
            LogTierContext.Set(LogTierProperties.UserName, userName);
        }

        private static void OnEndRequest(object sender, EventArgs e)
        {
            var context = ((HttpApplication)sender).Context;
            try
            {
                var stopwatch = context.Items[StopwatchItemKey] as Stopwatch;
                if (stopwatch != null) stopwatch.Stop();
                var elapsed = stopwatch != null ? Math.Round(stopwatch.Elapsed.TotalMilliseconds, 3) : (double?)null;
                var error = context.Server.GetLastError();
                var statusCode = error == null ? context.Response.StatusCode : 500;

                LogTierContext.Set(LogTierProperties.HttpStatusCode, statusCode);
                LogTierContext.Set(LogTierProperties.ResponseTimeMs, elapsed);

                if (Options.EmitHttpCompletionEvent)
                {
                    var properties = new Dictionary<string, object>
                    {
                        [LogTierProperties.EventTypeId] = LogTierEventTypes.HttpRequestCompleted,
                        [LogTierProperties.EventCategory] = LogTierEventCategories.ApplicationTransaction,
                        [LogTierProperties.EventStatusCode] = error == null ? "Completed" : "Failed",
                        [LogTierProperties.EventId] = Guid.NewGuid().ToString("D")
                    };
                    using (LogTierContext.Push(properties))
                    {
                        var message = "HTTP " + context.Request.HttpMethod + " " + context.Request.Path +
                                      " completed with status " + statusCode + " in " +
                                      (elapsed.HasValue ? elapsed.Value.ToString("0.###", CultureInfo.InvariantCulture) : "unknown") + " ms";
                        if (error == null) Logger.Info(message);
                        else Logger.Error(message, error);
                    }
                }
            }
            finally
            {
                LogTierContext.ClearRequestProperties();
            }
        }

        private static LogTierLog4NetOptions LoadOptions()
        {
            var options = new LogTierLog4NetOptions();
            options.ApplicationName = AppSetting("LogTier:ApplicationName") ?? options.ApplicationName;
            options.ApplicationVersion = AppSetting("LogTier:ApplicationVersion") ?? options.ApplicationVersion;
            options.EnvironmentName = AppSetting("LogTier:EnvironmentName") ?? options.EnvironmentName;
            options.DeviceIdentifier = AppSetting("LogTier:DeviceIdentifier") ?? options.DeviceIdentifier;
            options.DeviceMacAddress = AppSetting("LogTier:DeviceMacAddress") ?? options.DeviceMacAddress;
            options.IncludeHttpHeaders = ParseBool(AppSetting("LogTier:IncludeHttpHeaders"), options.IncludeHttpHeaders);
            options.IncludeNetworkEndpointData = ParseBool(AppSetting("LogTier:IncludeNetworkEndpointData"), options.IncludeNetworkEndpointData);
            options.EmitHttpCompletionEvent = ParseBool(AppSetting("LogTier:EmitHttpCompletionEvent"), options.EmitHttpCompletionEvent);

            long asn;
            if (long.TryParse(AppSetting("LogTier:AutonomousSystemNumber"), out asn)) options.AutonomousSystemNumber = asn;

            var allowList = AppSetting("LogTier:HeaderAllowList");
            if (!string.IsNullOrWhiteSpace(allowList))
            {
                options.HttpHeaderAllowList.Clear();
                foreach (var h in allowList.Split(new[] { ',' }, StringSplitOptions.RemoveEmptyEntries)) options.HttpHeaderAllowList.Add(h.Trim());
            }
            return options;
        }

        private static Dictionary<string, string> CaptureHeaders(NameValueCollection headers)
        {
            var result = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            foreach (var name in Options.HttpHeaderAllowList)
            {
                var value = headers[name];
                if (value == null) continue;
                result[name] = Options.RedactedHttpHeaders.Contains(name) ? "[REDACTED]" : value;
            }
            return result;
        }

        private static string ResolveTransactionId(HttpRequest request)
        {
            return Header(request, "X-Correlation-ID") ?? Header(request, "X-Request-ID") ?? Guid.NewGuid().ToString("D");
        }

        private static string Header(HttpRequest request, string name)
        {
            var value = request.Headers[name];
            return string.IsNullOrWhiteSpace(value) ? null : value;
        }

        private static Tuple<string, string> SplitIp(string value)
        {
            IPAddress address;
            if (!IPAddress.TryParse(value, out address)) return Tuple.Create<string, string>(null, null);
            if (address.IsIPv4MappedToIPv6) address = address.MapToIPv4();
            if (address.AddressFamily == AddressFamily.InterNetwork) return Tuple.Create(address.ToString(), (string)null);
            if (address.AddressFamily == AddressFamily.InterNetworkV6) return Tuple.Create((string)null, address.ToString());
            return Tuple.Create<string, string>(null, null);
        }

        private static object ParseInt(string value)
        {
            int parsed;
            return int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out parsed) ? (object)parsed : null;
        }

        private static bool ParseBool(string value, bool fallback)
        {
            bool parsed;
            return bool.TryParse(value, out parsed) ? parsed : fallback;
        }

        private static string AppSetting(string name) => ConfigurationManager.AppSettings[name];
    }
}
