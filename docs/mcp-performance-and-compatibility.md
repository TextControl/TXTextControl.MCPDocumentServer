# MCP performance and compatibility review

> Update: the persistent, isolated worker pool, FIFO session sequencing, atomic persistence, mutation recovery, bounded staging, health endpoints, metrics, integration tests, and benchmark harness described below have now been implemented. See [document-worker-pool.md](document-worker-pool.md) for the current architecture and measured results. The priority sections remain as historical rationale and for remaining deployment work.

## Implemented baseline

- Model Context Protocol .NET packages are aligned on 2.2.0 between server and client.
- Streamable HTTP remains stateless so MCP transport sessions do not require affinity.
- `create_document_export` produces a compact structured result and a range-capable binary download link.
- `get_as_base64` remains available for compatibility clients, but AI integrations can keep it out of model context.
- Tool failures set MCP `CallToolResult.isError` and also include a structured error payload.
- Session identifiers are restricted to portable, path-safe characters.
- Loopback host validation is the default development configuration.

## Priority 0: correctness under concurrency

Introduce a per-document-session execution gate or actor. Calls that touch one session must be ordered, while different sessions should execute concurrently. The current file-backed design can otherwise race when two tool calls load and save the same `document.tx` or `document.state.json` concurrently.

Add an optimistic `revision` to mutating requests and responses. Reject writes based on an old revision instead of silently overwriting a newer document. Add an optional idempotency key for retries of non-idempotent tools such as `apply_operations` and `create_document_export`.

## Priority 1: remove repeated document loading

The largest expected CPU cost is creating a new `ServerTextControl`, loading `document.tx`, performing one operation, and saving it for nearly every tool call. Use a bounded session-worker pool that owns warm `ServerTextControl` instances. Pin one document session to one worker while active, evict it after an idle timeout, and persist checkpoints asynchronously.

Keep the neutral `DocumentState` in the active session object. Persist compact JSON atomically only at checkpoints or after mutations; do not re-read and pretty-print the full state for every operation.

Measure worker count rather than assuming more concurrency is faster. Bound it according to TX Text Control licensing, memory consumption, and measured throughput.

## Priority 1: binary input and output

Add a matching streamed upload endpoint for source documents and images. Base64 import has the same allocation and transfer overhead as Base64 export. Use short-lived artifact identifiers, size limits, MIME validation, and content hashing.

For distributed deployment, place artifacts in shared object storage and return short-lived signed URLs. Do not return a server-local filesystem path to remote MCP clients.

## Priority 1: MCP contracts

Give every tool an explicit success response type, `outputSchema`, and accurate annotations (`readOnlyHint`, `destructiveHint`, `idempotentHint`, and `openWorldHint`). Keep errors in protocol-level `isError` results rather than encoding failure only as ordinary JSON.

Split the very large authoring guide into MCP resources or parameterized resource templates. Keep tool descriptions concise and return only the requested capability pack. This reduces discovery payloads and local-model prompt-evaluation cost while remaining discoverable.

Add pagination or bounded projections to tools that can return entire documents, paragraph collections, tables, or authoring metadata. Large successful results can dominate both network time and model context.

## Priority 1: deployment security

Require OAuth or an API gateway policy for `/mcp` and `/exports` in production. Scope session and export identifiers to the authenticated tenant. Configure a public base URI or forwarded-header policy behind a trusted reverse proxy instead of relying on arbitrary host headers.

Apply request-size, document-size, image-size, operation-count, and execution-time limits. Rate-limit document rendering separately from inexpensive inspection tools.

## Priority 2: horizontal scaling and observability

Abstract session metadata and artifact storage behind interfaces. Stateless MCP transport alone does not make the file-backed application state horizontally scalable; use shared storage or explicit session routing.

Instrument tool duration, queue time, TX load/save time, export time, bytes transferred, active sessions, worker-pool utilization, and failures by error code. Establish benchmark documents and report median and tail latency (p50, p95, p99) plus throughput and peak memory.
