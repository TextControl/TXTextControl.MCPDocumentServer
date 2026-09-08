# Session lifecycle and cleanup

Session creation and restoration are serialized within a server instance. Concurrent
requests restoring one persisted session receive the same session object and operation
lock. Creating an already-existing explicit session ID fails rather than overwriting
its state; load/get the existing session instead.

Cleanup considers directory age and the cached session's last access. Read-only use
therefore keeps a session alive even though it does not change the document files.
Cleanup skips sessions whose operation lock is held and does not follow symbolic-link
or junction session directories. Explicit deletion of a busy session fails with a
retry message. Failed deletion does not prematurely discard the cached session.

Expected storage I/O/access failures in the cleanup background service are logged and
retried at the next 15-minute interval instead of stopping the host. The default age
policy remains `McpServer:SessionMaxAgeHours`.

These are single-server lifecycle protections, not distributed locking or multi-tenant
authorization. Use an application-owned storage directory, restrict filesystem access,
and do not run independent servers against the same mutable session store.

The September 2026 hardening pass includes regression tests for concurrent restoration,
duplicate creation, active read-only sessions, and busy-session cleanup/deletion. The
complete MCP test suite passed (153 tests); existing nullable/XML documentation compiler
warnings still need a separate cleanup pass.
