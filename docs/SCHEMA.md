# LogTier JSON schema

The two packages deliberately emit the same JSON field names so logs from modern .NET services and .NET Framework 4.8 applications can be normalized in the same collector/SIEM pipeline.

## Top-level structure

```json
{
  "timestamp": "2026-09-07T22:55:12.123Z",
  "m2131": {
    "memorandum": "M-21-31",
    "event_logging_tier": "EL1",
    "schema_version": "1.0.0"
  },
  "event": {
    "id": "UUID",
    "type_id": "APP-AUTH-SUCCESS",
    "category": "ApplicationAuthentication",
    "status_code": "Success",
    "level": "Information",
    "message": "..."
  },
  "device": {
    "id": "asset-id-or-hostname",
    "mac_address": null,
    "hostname": "host01"
  },
  "session": {
    "id": null,
    "transaction_id": "correlation-id",
    "trace_id": null,
    "span_id": null
  },
  "network": {
    "autonomous_system_number": null,
    "source": { "ipv4": null, "ipv6": null, "port": null },
    "destination": { "ipv4": null, "ipv6": null, "port": null }
  },
  "http": {
    "method": null,
    "scheme": null,
    "host": null,
    "path": null,
    "status_code": null,
    "response_time_ms": null,
    "headers": null
  },
  "user": { "id": null, "name": null },
  "command": null,
  "application": {
    "name": "service-name",
    "version": "1.0.0",
    "environment": "Production",
    "source_context": "...",
    "process_id": 1234,
    "thread_id": 12
  },
  "exception": { "type": "...", "message": "...", "stack_trace": "...", "inner": null },
  "properties": {}
}
```

## M-21-31 EL1 application-field mapping

| M-21-31 minimum/application data when applicable | LogTier field |
|---|---|
| Timestamp | `timestamp` |
| Event/type/status code | `event.type_id`, `event.status_code`, `http.status_code` |
| Device identifier | `device.id`; optional `device.mac_address` |
| Session/transaction identifier | `session.id`, `session.transaction_id` |
| Autonomous System Number | `network.autonomous_system_number` |
| Source IPv4/IPv6 | `network.source.ipv4`, `network.source.ipv6` |
| Destination IPv4/IPv6 | `network.destination.ipv4`, `network.destination.ipv6` |
| Response time | `http.response_time_ms` |
| Additional headers | `http.headers` (allow-list/redaction enforced by HTTP integrations) |
| Username/User ID | `user.name`, `user.id` |
| Command executed | `command` |
| Key-value/structured data | entire JSON object and `properties` |
| Unique event identifier | `event.id` |

Not every field is meaningful for every event. The packages preserve explicit `null` fields in the normalized schema rather than fabricating data.

## Event type identifiers

Known helpers use explicit IDs, including:

- `APP-STARTED`
- `APP-STOPPED`
- `APP-FAILURE`
- `APP-CONFIG-CHANGED`
- `APP-HTTP-REQUEST-COMPLETED`
- `APP-AUTH-SUCCESS`
- `APP-AUTH-FAILURE`
- `APP-ACCOUNT-CHANGED`
- `APP-PRIVILEGE-USED`
- `APP-TRANSACTION`

Domain/security events should define their own stable IDs. Serilog generic events receive an automatic SHA-256-derived type ID based on source context + message template. log4net does not retain a Serilog-style message template, so generic events receive an automatic type ID based on logger and level (plus calling class and method when `IncludeLocationInEventTypeId` is on). Explicit event type IDs are preferred for audit-relevant events.

## Security considerations

Do not log credentials, tokens, session cookies, API keys, private cryptographic material, or unnecessary sensitive payloads. The built-in HTTP integrations use an allow-list and redaction, but application-defined properties remain the application's responsibility.

## Operational controls outside the packages

M-21-31 includes controls beyond application event formatting. Production architecture must separately address applicable centralized/near-real-time forwarding, authenticated/encrypted transport, access controls, log integrity and validation, retention, backup/replay, failure monitoring, authoritative time synchronization, and other required telemetry sources.
