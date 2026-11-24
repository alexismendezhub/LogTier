using System;
using System.Collections.Generic;
using log4net;

namespace LogTier.Log4Net
{
    /// <summary>Helpers for adding M-21-31 properties to log4net LogicalThreadContext.</summary>
    public static class LogTierContext
    {
        private static readonly string[] RequestPropertyNames =
        {
            LogTierProperties.SessionId, LogTierProperties.TransactionId, LogTierProperties.SourceIpv4,
            LogTierProperties.SourceIpv6, LogTierProperties.SourcePort, LogTierProperties.DestinationIpv4,
            LogTierProperties.DestinationIpv6, LogTierProperties.DestinationPort, LogTierProperties.HttpHeaders,
            LogTierProperties.HttpMethod, LogTierProperties.HttpScheme, LogTierProperties.HttpHost,
            LogTierProperties.HttpPath, LogTierProperties.HttpStatusCode, LogTierProperties.ResponseTimeMs,
            LogTierProperties.UserId, LogTierProperties.UserName, LogTierProperties.Command,
            LogTierProperties.TraceId, LogTierProperties.SpanId
        };

        /// <summary>Sets a property on the log4net logical thread context; a null value removes it.</summary>
        public static void Set(string propertyName, object value)
        {
            if (string.IsNullOrWhiteSpace(propertyName)) throw new ArgumentException("Property name is required.", nameof(propertyName));
            if (value == null) LogicalThreadContext.Properties.Remove(propertyName);
            else LogicalThreadContext.Properties[propertyName] = value;
        }

        /// <summary>Sets several context properties and restores their previous values when the returned scope is disposed.</summary>
        public static IDisposable Push(IDictionary<string, object> properties)
        {
            if (properties == null) throw new ArgumentNullException(nameof(properties));
            return new PropertyScope(properties);
        }

        internal static void ClearRequestProperties()
        {
            foreach (var name in RequestPropertyNames) LogicalThreadContext.Properties.Remove(name);
        }

        private sealed class PropertyScope : IDisposable
        {
            private readonly Dictionary<string, object> _previous = new Dictionary<string, object>(StringComparer.Ordinal);
            private readonly HashSet<string> _previouslyMissing = new HashSet<string>(StringComparer.Ordinal);
            private bool _disposed;

            public PropertyScope(IDictionary<string, object> properties)
            {
                foreach (var pair in properties)
                {
                    var prior = LogicalThreadContext.Properties[pair.Key];
                    if (prior == null) _previouslyMissing.Add(pair.Key);
                    else _previous[pair.Key] = prior;

                    if (pair.Value == null) LogicalThreadContext.Properties.Remove(pair.Key);
                    else LogicalThreadContext.Properties[pair.Key] = pair.Value;
                }
            }

            public void Dispose()
            {
                if (_disposed) return;
                _disposed = true;
                foreach (var name in _previouslyMissing) LogicalThreadContext.Properties.Remove(name);
                foreach (var pair in _previous) LogicalThreadContext.Properties[pair.Key] = pair.Value;
            }
        }
    }
}
