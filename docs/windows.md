[← Usage index](Usage.md)

# windows — find windows

Lists windows with **DWM-accurate geometry** — the rect as actually drawn, not the oversized one
`GetWindowRect` reports. The hwnd it returns is what you feed to [`capture`](capture.md), [`snapshot`](snapshot.md), and
[`input`](input.md) as `win:<hwnd>`.

```powershell
windeskctl windows list                       # every window
windeskctl windows list --title notepad       # title contains "notepad" (case-insensitive)
windeskctl windows list --process chrome      # from the chrome process
windeskctl windows list --json                # JSON instead of a table
```

In the table, a `*` marks the foreground window. Filter rather than dumping everything when you know
what you want.

MCP: tool `windows`, arguments `titleContains`, `processName`, `includeMinimized` (default `true`).

---

# windows &lt;action&gt; / window_action — move and manage windows

Focus, move, resize, minimize, maximize, or restore a window. Move and resize coordinates address
the **visible** edges of the window — windeskctl converts to the raw rect Win32 wants, so what you ask
for is what you see.

```powershell
windeskctl windows focus 12345
windeskctl windows move 12345 --x 100 --y -1000        # visible top-left corner
windeskctl windows resize 12345 --width 800 --height 600
windeskctl windows minimize 12345
windeskctl windows maximize 12345
windeskctl windows restore 12345
```

MCP collapses these into one tool `window_action` with an `action` argument
(`focus | move | resize | minimize | maximize | restore`) plus optional `x`, `y`, `width`, `height`
for move/resize.

Both surfaces **re-read the window afterwards and report its actual state**, which can differ from
what you asked for because Windows clamps to screen bounds and snaps to edges. Trust the reported
rect, not the request.
