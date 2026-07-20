[← Usage index](Usage.md)

# mcp — run as a server

```powershell
windeskctl mcp
```

Serves the Model Context Protocol over stdio. Register it with an MCP client by pointing the client
at `windeskctl.exe` with the `mcp` argument. It exposes seven tools: [`doctor`](doctor.md), [`windows`](windows.md),
[`window_action`](windows.md), [`capture`](capture.md), [`record`](record.md),
[`snapshot`](snapshot.md), [`input`](input.md).

There is no port and no socket — the transport is stdio, so the OS process boundary is the security
model. Logs go to stderr; stdout is the protocol.
