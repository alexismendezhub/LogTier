using System.Buffers;
using System.Globalization;
using System.Text;
using System.Text.Json;
using Serilog.Events;
using Serilog.Formatting;

namespace LogTier.Serilog;

/// <summary>
/// Emits one newline-delimited JSON object per Serilog event using the package M-21-31 schema.
/// </summary>
/// <remarks>
/// The JSON is streamed straight to a reused per-thread buffer with <see cref="Utf8JsonWriter"/>,
/// so no intermediate dictionaries are allocated for each event.
/// </remarks>
public sealed class LogTierJsonFormatter : ITextFormatter
{
    // Exceptions nested deeper than this are cut off so a self-referencing chain cannot loop.
    private const int MaxInnerExceptionDepth = 8;

    [ThreadStatic]
    private static ArrayBufferWriter<byte>? _buffer;

    [ThreadStatic]
    private static Utf8JsonWriter? _writer;

    private readonly LogTierOptions _options;

    /// <summary>Creates the formatter with the given options.</summary>
    public LogTierJsonFormatter(LogTierOptions options)
        => _options = options ?? throw new ArgumentNullException(nameof(options));

    /// <inheritdoc />
    public void Format(LogEvent logEvent, TextWriter output)
    {
        ArgumentNullException.ThrowIfNull(logEvent);
        ArgumentNullException.ThrowIfNull(output);

        var buffer = _buffer ??= new ArrayBufferWriter<byte>(1024);
        buffer.Clear();
        var writer = _writer ??= new Utf8JsonWriter(buffer);
        writer.Reset(buffer);

        try
        {
            Write(writer, logEvent);
            writer.Flush();
            output.Write(Encoding.UTF8.GetString(buffer.WrittenSpan));
            output.Write(Environment.NewLine);
        }
        finally
        {
            // A writer left mid-object by an exception must not leak state into the next event on this thread.
            writer.Reset(buffer);
            buffer.Clear();
        }
    }

    private void Write(Utf8JsonWriter writer, LogEvent logEvent)
    {
        var p = logEvent.Properties;

        writer.WriteStartObject();
        writer.WriteString("timestamp", logEvent.Timestamp.UtcDateTime.ToString("yyyy-MM-dd'T'HH:mm:ss.fff'Z'", CultureInfo.InvariantCulture));

        writer.WriteStartObject("m2131");
        WriteString(writer, "memorandum", GetString(p, LogTierProperties.Memorandum) ?? LogTierStandard.Memorandum);
        WriteString(writer, "event_logging_tier", GetString(p, LogTierProperties.EventLoggingTier) ?? LogTierStandard.EventLoggingTier);
        WriteString(writer, "schema_version", GetString(p, LogTierProperties.SchemaVersion) ?? _options.SchemaVersion);
        writer.WriteEndObject();

        writer.WriteStartObject("event");
        WriteString(writer, "id", GetString(p, LogTierProperties.EventId));
        WriteString(writer, "type_id", GetString(p, LogTierProperties.EventTypeId));
        WriteString(writer, "category", GetString(p, LogTierProperties.EventCategory));
        WriteProperty(writer, "status_code", p, LogTierProperties.EventStatusCode);
        writer.WriteString("level", logEvent.Level.ToString());
        writer.WriteString("message", logEvent.RenderMessage(CultureInfo.InvariantCulture));
        writer.WriteString("message_template", logEvent.MessageTemplate.Text);
        writer.WriteEndObject();

        writer.WriteStartObject("device");
        WriteString(writer, "id", GetString(p, LogTierProperties.DeviceId));
        WriteString(writer, "mac_address", GetString(p, LogTierProperties.DeviceMac));
        WriteString(writer, "hostname", GetString(p, LogTierProperties.HostName));
        writer.WriteEndObject();

        writer.WriteStartObject("session");
        WriteString(writer, "id", GetString(p, LogTierProperties.SessionId));
        WriteString(writer, "transaction_id", GetString(p, LogTierProperties.TransactionId));
        WriteString(writer, "trace_id", GetString(p, LogTierProperties.TraceId));
        WriteString(writer, "span_id", GetString(p, LogTierProperties.SpanId));
        writer.WriteEndObject();

        writer.WriteStartObject("network");
        WriteProperty(writer, "autonomous_system_number", p, LogTierProperties.AutonomousSystemNumber);
        writer.WriteStartObject("source");
        WriteString(writer, "ipv4", GetString(p, LogTierProperties.SourceIpv4));
        WriteString(writer, "ipv6", GetString(p, LogTierProperties.SourceIpv6));
        WriteProperty(writer, "port", p, LogTierProperties.SourcePort);
        writer.WriteEndObject();
        writer.WriteStartObject("destination");
        WriteString(writer, "ipv4", GetString(p, LogTierProperties.DestinationIpv4));
        WriteString(writer, "ipv6", GetString(p, LogTierProperties.DestinationIpv6));
        WriteProperty(writer, "port", p, LogTierProperties.DestinationPort);
        writer.WriteEndObject();
        writer.WriteEndObject();

        writer.WriteStartObject("http");
        WriteString(writer, "method", GetString(p, LogTierProperties.HttpMethod));
        WriteString(writer, "scheme", GetString(p, LogTierProperties.HttpScheme));
        WriteString(writer, "host", GetString(p, LogTierProperties.HttpHost));
        WriteString(writer, "path", GetString(p, LogTierProperties.HttpPath));
        WriteProperty(writer, "status_code", p, LogTierProperties.HttpStatusCode);
        WriteProperty(writer, "response_time_ms", p, LogTierProperties.ResponseTimeMs);
        WriteProperty(writer, "headers", p, LogTierProperties.HttpHeaders);
        writer.WriteEndObject();

        writer.WriteStartObject("user");
        WriteString(writer, "id", GetString(p, LogTierProperties.UserId));
        WriteString(writer, "name", GetString(p, LogTierProperties.UserName));
        writer.WriteEndObject();

        WriteString(writer, "command", GetString(p, LogTierProperties.Command));

        writer.WriteStartObject("application");
        WriteString(writer, "name", GetString(p, LogTierProperties.ApplicationName));
        WriteString(writer, "version", GetString(p, LogTierProperties.ApplicationVersion));
        WriteString(writer, "environment", GetString(p, LogTierProperties.EnvironmentName));
        WriteString(writer, "source_context", GetString(p, "SourceContext"));
        WriteProperty(writer, "process_id", p, LogTierProperties.ProcessId);
        WriteProperty(writer, "thread_id", p, LogTierProperties.ThreadId);
        writer.WriteEndObject();

        writer.WritePropertyName("exception");
        WriteException(writer, logEvent.Exception, 0);

        writer.WriteStartObject("properties");
        foreach (var property in p)
        {
            if (!property.Key.StartsWith("LogTier", StringComparison.Ordinal))
            {
                writer.WritePropertyName(property.Key);
                WriteValue(writer, property.Value);
            }
        }

        writer.WriteEndObject();
        writer.WriteEndObject();
    }

    private static void WriteException(Utf8JsonWriter writer, Exception? exception, int depth)
    {
        if (exception is null || depth > MaxInnerExceptionDepth)
        {
            writer.WriteNullValue();
            return;
        }

        writer.WriteStartObject();
        WriteString(writer, "type", exception.GetType().FullName);
        writer.WriteString("message", exception.Message);
        WriteString(writer, "stack_trace", exception.StackTrace);
        if (exception.InnerException is not null)
        {
            writer.WritePropertyName("inner");
            WriteException(writer, exception.InnerException, depth + 1);
        }

        writer.WriteEndObject();
    }

    private static void WriteString(Utf8JsonWriter writer, string name, string? value)
    {
        if (value is null)
        {
            writer.WriteNull(name);
        }
        else
        {
            writer.WriteString(name, value);
        }
    }

    private static void WriteProperty(Utf8JsonWriter writer, string name, IReadOnlyDictionary<string, LogEventPropertyValue> properties, string key)
    {
        writer.WritePropertyName(name);
        if (properties.TryGetValue(key, out var value))
        {
            WriteValue(writer, value);
        }
        else
        {
            writer.WriteNullValue();
        }
    }

    private static string? GetString(IReadOnlyDictionary<string, LogEventPropertyValue> properties, string name)
    {
        if (!properties.TryGetValue(name, out var value))
        {
            return null;
        }

        return value is ScalarValue scalar
            ? Convert.ToString(scalar.Value, CultureInfo.InvariantCulture)
            : value.ToString(null, CultureInfo.InvariantCulture);
    }

    private static void WriteValue(Utf8JsonWriter writer, LogEventPropertyValue value)
    {
        switch (value)
        {
            case ScalarValue scalar:
                WriteScalar(writer, scalar.Value);
                break;
            case SequenceValue sequence:
                writer.WriteStartArray();
                foreach (var element in sequence.Elements)
                {
                    WriteValue(writer, element);
                }

                writer.WriteEndArray();
                break;
            case StructureValue structure:
                writer.WriteStartObject();
                foreach (var property in structure.Properties)
                {
                    writer.WritePropertyName(property.Name);
                    WriteValue(writer, property.Value);
                }

                writer.WriteEndObject();
                break;
            case DictionaryValue dictionary:
                writer.WriteStartObject();
                foreach (var element in dictionary.Elements)
                {
                    writer.WritePropertyName(Convert.ToString(element.Key.Value, CultureInfo.InvariantCulture) ?? string.Empty);
                    WriteValue(writer, element.Value);
                }

                writer.WriteEndObject();
                break;
            default:
                writer.WriteStringValue(value.ToString(null, CultureInfo.InvariantCulture));
                break;
        }
    }

    private static void WriteScalar(Utf8JsonWriter writer, object? value)
    {
        switch (value)
        {
            case null:
                writer.WriteNullValue();
                break;
            case string text:
                writer.WriteStringValue(text);
                break;
            case bool flag:
                writer.WriteBooleanValue(flag);
                break;
            case int number:
                writer.WriteNumberValue(number);
                break;
            case long number:
                writer.WriteNumberValue(number);
                break;
            case short number:
                writer.WriteNumberValue(number);
                break;
            case byte number:
                writer.WriteNumberValue(number);
                break;
            case sbyte number:
                writer.WriteNumberValue(number);
                break;
            case ushort number:
                writer.WriteNumberValue(number);
                break;
            case uint number:
                writer.WriteNumberValue(number);
                break;
            case ulong number:
                writer.WriteNumberValue(number);
                break;
            case decimal number:
                writer.WriteNumberValue(number);
                break;
            case double number when double.IsFinite(number):
                writer.WriteNumberValue(number);
                break;
            case float number when float.IsFinite(number):
                writer.WriteNumberValue(number);
                break;
            case DateTime dateTime:
                writer.WriteStringValue(dateTime);
                break;
            case DateTimeOffset dateTimeOffset:
                writer.WriteStringValue(dateTimeOffset);
                break;
            case Guid guid:
                writer.WriteStringValue(guid);
                break;
            case IFormattable formattable:
                writer.WriteStringValue(formattable.ToString(null, CultureInfo.InvariantCulture));
                break;
            default:
                writer.WriteStringValue(value.ToString());
                break;
        }
    }
}
