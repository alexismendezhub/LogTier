using System;
using System.Collections.Generic;
using System.IO;
using log4net;
using log4net.Appender;
using log4net.Repository.Hierarchy;
using LogTier.Log4Net;
using Newtonsoft.Json.Linq;
using Xunit;

namespace LogTier.Log4Net.IntegrationTests;

/// <summary>End-to-end contract: log4net events, context, exceptions and captured Trace/Console/EventSource output all come out as M-21-31 EL1 JSON.</summary>
public sealed class LogTierLog4NetTests
{
    [Fact]
    public void Events_and_captured_output_follow_the_EL1_contract()
    {
        var hierarchy = (Hierarchy)LogManager.GetRepository();
        hierarchy.Root.RemoveAllAppenders();
        var memory = new MemoryAppender();
        memory.ActivateOptions();
        hierarchy.Root.AddAppender(memory);
        hierarchy.Root.Level = log4net.Core.Level.All;
        hierarchy.Configured = true;

        var log = LogManager.GetLogger("LogTier.Log4Net.IntegrationTests");
        using (LogTierContext.Push(new Dictionary<string, object>
        {
            [LogTierProperties.TransactionId] = "txn-123",
            [LogTierProperties.SourceIpv4] = "192.0.2.10",
            [LogTierProperties.DestinationIpv4] = "198.51.100.20",
            ["Value"] = 42
        }))
        {
            log.LogTierInfo("SMOKE-TEST", "ApplicationTransaction", "Success", "Smoke test");
        }

        var events = memory.GetEvents();
        Require(events.Length == 1, "one event");

        var options = new LogTierLog4NetOptions
        {
            ApplicationName = "LogTier.Log4Net.IntegrationTests",
            ApplicationVersion = "1.0.0",
            EnvironmentName = "Test",
            DeviceIdentifier = "device-test-001"
        };

        var writer = new StringWriter();
        new LogTierJsonLayout(options).Format(writer, events[0]);
        var root = Parse(writer.ToString());

        Require((string)root["m2131"]["memorandum"] == "M-21-31", "memorandum");
        Require((string)root["m2131"]["event_logging_tier"] == "EL1", "EL1 tier");
        Require(((string)root["timestamp"]).EndsWith("Z", StringComparison.Ordinal), "UTC timestamp");
        Require((string)root["event"]["type_id"] == "SMOKE-TEST", "event type ID");
        Require(!string.IsNullOrWhiteSpace((string)root["event"]["id"]), "event ID");
        Require((string)root["device"]["id"] == "device-test-001", "device ID");
        Require((string)root["session"]["transaction_id"] == "txn-123", "transaction ID");
        Require((string)root["network"]["source"]["ipv4"] == "192.0.2.10", "source IPv4");
        Require((string)root["network"]["destination"]["ipv4"] == "198.51.100.20", "destination IPv4");
        Require((int)root["properties"]["Value"] == 42, "custom property");
        Require(root["properties"]["log4net:UserName"] == null, "log4net internal properties left out");

        var layout = new LogTierJsonLayout(options);
        Require(!layout.IgnoresException, "layout writes the exception itself so the appender keeps one JSON object per line");

        memory.Clear();
        log.Error("Failed", new InvalidOperationException("outer", new ArgumentException("inner")));
        log.Error("Failed");
        var failure = memory.GetEvents();
        var failureJson = Format(layout, failure[0]);
        Require(failureJson.Split('\n').Length == 2 && failureJson.EndsWith(Environment.NewLine, StringComparison.Ordinal), "single JSON line");
        var failureRoot = Parse(failureJson);
        Require((string)failureRoot["exception"]["inner"]["message"] == "inner", "inner exception");
        Require(failureRoot["exception"]["inner"]["inner"] == null, "inner exception chain ends");
        var otherRoot = Parse(Format(layout, failure[1]));
        Require((string)failureRoot["event"]["type_id"] == (string)otherRoot["event"]["type_id"], "stable automatic event type ID");
        Require(((string)failureRoot["event"]["type_id"]).StartsWith("AUTO-", StringComparison.Ordinal), "automatic event type ID prefix");

        // Output other libraries write outside log4net comes back as LogTier events.
        layout.ActivateOptions();
        Require(LogTierCapture.IsStarted, "capture on by default");
        memory.Clear();
        System.Diagnostics.Trace.TraceWarning("trace {0}", "warning");
        Console.WriteLine("console line");
        Console.Error.WriteLine("error line");
        SmokeEventSource.Log.Warn("event source");
        new LogTierLog4NetLoggerProvider().CreateLogger("Library.UsingMel").Log(
            Microsoft.Extensions.Logging.LogLevel.Warning, new Microsoft.Extensions.Logging.EventId(7), "mel line", null, (state, _) => state);
        LogTierCapture.Stop();
        var captured = memory.GetEvents();
        RequireCaptured(layout, captured, "Trace", "trace warning", log4net.Core.Level.Warn, "Trace");
        RequireCaptured(layout, captured, "Console", "console line", log4net.Core.Level.Info, "Console");
        RequireCaptured(layout, captured, "Console.Error", "error line", log4net.Core.Level.Warn, "Console");
        RequireCaptured(layout, captured, "EventSource.LogTier-Smoke", "Smoke event source", log4net.Core.Level.Warn, "EventSource");
        RequireCaptured(layout, captured, "Library.UsingMel", "mel line", log4net.Core.Level.Warn, "MicrosoftExtensionsLogging");

        Console.WriteLine("LogTier.Log4Net smoke test passed.");
        Console.WriteLine(writer.ToString());
    }

    private static void RequireCaptured(LogTierJsonLayout layout, log4net.Core.LoggingEvent[] events, string loggerPrefix, string message, log4net.Core.Level level, string captureSource)
    {
        log4net.Core.LoggingEvent match = null;
        foreach (var e in events)
        {
            if (e.LoggerName.StartsWith(loggerPrefix, StringComparison.Ordinal) && e.RenderedMessage == message) match = e;
        }
        Require(match != null && match.Level == level, "captured " + loggerPrefix + " output");
        var root = Parse(Format(layout, match));
        Require((string)root["m2131"]["event_logging_tier"] == "EL1", loggerPrefix + " output in LogTier format");
        Require((string)root["properties"]["CaptureSource"] == captureSource, loggerPrefix + " capture source");
    }

    // Dates stay strings so the assertions see exactly what the layout wrote.
    private static JObject Parse(string json)
    {
        using (var reader = new Newtonsoft.Json.JsonTextReader(new StringReader(json)) { DateParseHandling = Newtonsoft.Json.DateParseHandling.None })
            return JObject.Load(reader);
    }

    private static string Format(LogTierJsonLayout layout, log4net.Core.LoggingEvent loggingEvent)
    {
        var writer = new StringWriter();
        layout.Format(writer, loggingEvent);
        return writer.ToString();
    }

    private static void Require(bool condition, string description)
    {
        if (!condition) throw new InvalidOperationException("Smoke test failed: " + description + ".");
    }
}

[System.Diagnostics.Tracing.EventSource(Name = "LogTier-Smoke")]
internal sealed class SmokeEventSource : System.Diagnostics.Tracing.EventSource
{
    public static readonly SmokeEventSource Log = new SmokeEventSource();

    [System.Diagnostics.Tracing.Event(1, Level = System.Diagnostics.Tracing.EventLevel.Warning, Message = "Smoke {0}")]
    public void Warn(string value) => WriteEvent(1, value);
}
