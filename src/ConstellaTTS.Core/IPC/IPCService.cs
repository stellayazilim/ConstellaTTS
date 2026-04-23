// ═══════════════════════════════════════════════════════════════════════
// OBSOLETE — DELETE THIS FILE
// ═══════════════════════════════════════════════════════════════════════
//
// This IPCService was the old synchronous stdin/stdout implementation
// (single IPCMessage type, event-based dispatch, in-band tts.chunk
// watchdog). The SDK.IPC project has been rewritten around Windows
// Named Pipes + ProactorEventLoop (IOCP) — see IPCClient.cs in SDK.IPC,
// which already implements IIPCService. Core now registers IPCClient
// directly; no Core-side adapter is needed.
// ═══════════════════════════════════════════════════════════════════════
