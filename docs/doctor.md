[← Usage index](Usage.md)

# doctor — self-test the machine

Reports virtual-desktop bounds, every monitor's origin / size / DPI, and the result of each internal
check. This is where you read monitor ids for [`capture`](capture.md), and the first thing to run when anything
coordinate-related looks wrong.

```powershell
windeskctl doctor              # human-readable summary
windeskctl doctor --json       # the raw report as JSON
windeskctl doctor --intrusive  # add checks that disturb the desktop (may steal focus, pop Start)
```

MCP: tool `doctor`, argument `includeIntrusive` (default `false`).

The `--intrusive` checks actually inject input to prove input works end-to-end, so they can steal
focus. Leave them off unless a plain `doctor` passes but real input still misbehaves. Exit code is
`0` when every check passes, `1` otherwise.
