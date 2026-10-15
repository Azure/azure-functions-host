# Worker Proxy System Log Pipeline

## Overview

Worker Proxy intercepts worker-originated `RpcLog(System)` messages and formats them using the existing Functions logs schema with the `MS_FUNCTIONS_WORKER_POD_LOGS` prefix.

Formatted records are placed in a bounded in-memory queue. A single background writer writes them to Worker Proxy stdout for Podr, Fluent Bit, and Legion ingestion.

## Flow

```text
Worker RpcLog(System)
        |
        v
Format prefixed record
        |
        v
Non-blocking queue admission
   |                       |
Accepted                 Rejected
   |                       |
Background writer        Drop record
   |
   v
Worker Proxy stdout
   |
   v
FunctionsWorkerPodLogs
```

FunctionRpc never waits for physical stdout output or queue capacity.

## Mode Behavior

| Mode | Worker Proxy output | Forward to Host |
| --- | --- | --- |
| `Disabled` | No | Yes |
| `Mirror` | Attempt enqueue | Yes |
| `Consume` | Attempt enqueue | No |
| `Consume` with queue rejection | Record dropped | No |

## Initial Constants

| Setting | Value |
| --- | ---: |
| Queue capacity | `16,000` records |
| Queue admission | Immediate and non-blocking |
| Queue readers | `1` |
| Queue writers | Multiple |
| Shutdown drain timeout | `5 seconds` |
| Details field limit | `10,000` characters |
| Output prefix | `MS_FUNCTIONS_WORKER_POD_LOGS ` |
| Stable source | `Worker.LanguageWorker` |
| Host fallback in `Consume` | None |

## Reliability

- The bounded queue prevents unlimited memory growth.
- A single reader preserves accepted-record ordering.
- A single writer prevents prefixed records from interleaving.
- Slow stdout does not delay invocation or control messages.
- Queue-full, faulted, or stopping records are dropped in `Consume`.
- Shutdown attempts to drain accepted records for up to five seconds.

This is a best-effort logging pipeline and does not guarantee lossless delivery.

## Future: Rate Limiting

Rate limiting can be added before queue admission if worker log volume creates sustained queue pressure.

The future policy can:

- aggressively limit or sample `Trace` and `Debug`;
- moderately limit `Information`;
- preserve higher limits for `Warning`;
- prioritize `Error` and `Critical`;
- expose rate-limited and dropped-record counters.

Rate limiting will reduce log storms and stabilize queue backpressure. It will complement the bounded background pipeline rather than replace it, because stdout can still become slow or unavailable.
