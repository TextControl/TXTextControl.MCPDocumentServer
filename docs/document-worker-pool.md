# Persistent document worker pool

## Architecture

The MCP host owns one application-lifetime `DocumentWorkerPool`. MCP tool and workflow contracts are unchanged; `WorkerPooledDocumentEngine` implements the existing `ITxDocumentEngine` contract and translates each validated call into a closed, typed worker command.

Each configured child process creates exactly one `ServerTextControl` during startup. The process executes one command at a time and keeps its most recently used session document loaded. The host routes an active session back to that worker when possible. An idle maintenance loop evicts affinity and unloads the document after `SessionAffinityIdleSeconds`; loading another session also replaces the hot document. A worker is never reserved permanently for a client or session.

The transport is protocol version 1: a four-byte little-endian length followed by bounded UTF-8 JSON. Requests carry a request ID, optional mutation ID, command, session ID, expected revision, deadline, mutation flag, and typed payload. Responses carry correlation and mutation IDs, structured success/error data, the new revision, and load/execute/save/serialization timing.

Large uploaded documents are decoded once by the host into a bounded, MCP-owned staging file. The worker receives only the validated local path. Normal exports are also written directly to MCP-managed storage and downloaded over the existing `/exports/...` endpoint. Base64 output remains available for compatibility.

## Ordering and durability

Every session has a FIFO task chain in the host. It guarantees submission order and prevents concurrent operations on one session. Different sessions can use different workers concurrently. The queue and worker counts are bounded.

The canonical document is always `document.tx`. Successful mutations save to a same-directory temporary file, flush it, and atomically replace the previous file. Session metadata uses the same pattern. Revisions advance only after a worker reports a successful commit.

Every mutation has a stable mutation ID. Before replying, the worker atomically persists the successful response as `document.worker.commit.json`. If the worker dies after saving but before its reply arrives, the host restarts it and recovers that exact response when the mutation ID matches. It never blindly retries an ambiguous mutation. On MCP restart, `DocumentSessionService` reconciles a newer commit record with `document.state.json`, while the TX snapshot remains authoritative.

Read-only calls may be retried once after worker replacement. A hard timeout closes and replaces the affected process. Worker stderr is retained as a bounded diagnostic tail.

## Process lifecycle

Workers are the same cross-platform MCP executable started with the private `--document-worker` switch. Standard input closing requests normal shutdown and disposes the persistent control. The hosted pool stops all children during application shutdown and escalates to process-tree termination after a grace period. Each worker also watches the parent PID and exits when the host disappears; a broken stdin pipe provides a second abnormal-parent-exit signal.

`WorkerExecutablePath` is optional and intended for integration testing or specialized deployment. A `.dll` value is launched through `dotnet`; otherwise the configured app host is executed directly.

## Configuration

```json
"DocumentWorkerPool": {
  "Enabled": true,
  "WorkerCount": 2,
  "InteractiveWorkerCount": 0,
  "QueueCapacity": 128,
  "StartupTimeoutSeconds": 30,
  "CommandTimeoutSeconds": 180,
  "SessionAffinityIdleSeconds": 120,
  "MaximumHotSessions": 2,
  "WorkerRestartLimit": 5,
  "WorkerRestartBackoffMilliseconds": 500,
  "MaximumInputMegabytes": 64,
  "MaximumOutputMegabytes": 128
}
```

Start with two workers and tune with representative documents. Every worker holds a native engine and can materially increase memory use. Set `Enabled` to `false` to run the legacy in-process engine for diagnosis and benchmark comparison.

`InteractiveWorkerCount` can reserve the first N workers for short reads and edits so long conversions cannot occupy the entire pool. It defaults to zero because reserving capacity reduces batch throughput; it must be less than `WorkerCount`.

## Health, diagnostics, and metrics

- `GET /health/document-workers` returns a non-authenticated aggregate health result suitable for probes.
- `GET /admin/document-workers` returns authenticated per-worker PID, health, restart count, hot session, last use, and working-set information.
- The `TxTextControl.McpServer.DocumentWorkers` meter records queue depth/wait, active commands, worker health/start/restart/failure, cache and affinity hits/misses/evictions, input/output sizes, load/save/execute/serialize durations, timeouts, retries, and recovered commits.
- Debug logs include command, worker index, queue time, and execution time. Worker failures include the bounded stderr tail in server diagnostics.

## Tests

`DocumentWorkerProtocolTests` verifies framing, bounds, version/correlation preservation, and configuration validation. `DocumentWorkerPoolIntegrationTests` runs real child processes and verifies FIFO same-session mutations, durable revision recovery, forced worker-crash recovery, and orphan-free shutdown. The existing MCP/document suite remains unchanged and exercises contract compatibility.

## Benchmarking

Run one server mode at a time, then point the benchmark project at it:

```powershell
# Worker pool (default)
dotnet run --project TxTextControl.McpServer.csproj
dotnet run --project Benchmarks/TxTextControl.McpServer.Benchmarks.csproj -- --url http://127.0.0.1:5000/mcp --label worker-pool --output docs/benchmarks/worker-pool.json

# Baseline in a new shell/server process
$env:DocumentWorkerPool__Enabled = "false"
dotnet run --project TxTextControl.McpServer.csproj
dotnet run --project Benchmarks/TxTextControl.McpServer.Benchmarks.csproj -- --url http://127.0.0.1:5000/mcp --label legacy-direct --output docs/benchmarks/legacy-direct.json
```

Pass `--pid <server-pid>` to include host working-set memory. The worker health endpoint supplies aggregate child working-set memory automatically.

The checked-in September 4, 2026 workstation run used two workers and had zero failures. The most relevant results were:

| Scenario | Legacy direct | Worker pool | Observation |
|---|---:|---:|---|
| Cold small create p50 | 332.87 ms | 361.28 ms | IPC and durable commit add latency |
| Ten same-session append edits p50 | 12.24 ms | 16.54 ms | Durable mutation journal is visible on small edits |
| Concurrent independent exports p95 | 40.87 ms | 31.19 ms | Bounded parallel workers reduce tail latency |
| Concurrent independent throughput | 144.04/s | 187.91/s | About 30% higher in this run |
| Large PDF export p50 | 165.49 ms | 161.61 ms | Rendering dominates; effectively similar |
| Total measured working set | 118.5 MB | 257.6 MB | Isolation and two native workers cost memory |

These results intentionally do not claim a universal speedup. On this machine, small serial calls are faster in-process, while cross-session concurrency and failure isolation improve with the pool. Expensive render/conversion time remains dominated by TX Text Control. Use the JSON reports and the meter data to tune worker count for the actual workload.
