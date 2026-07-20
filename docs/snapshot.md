[← Usage index](Usage.md)

# snapshot — read the UI as a tree

Reads a window's UI as a tree of named, typed elements. This is the **default** way to see what's on
screen. Each element gets an opaque `elem:<handle>` you can act on directly with [`input`](input.md) — no
coordinates, so you cannot miss.

```powershell
windeskctl snapshot win:12345                      # interactive elements, as a tree
windeskctl snapshot win:12345 --all                # include non-interactive layout elements
windeskctl snapshot win:12345 --max-depth 6        # walk shallower
windeskctl snapshot win:12345 --json               # JSON instead of a tree
windeskctl snapshot elem:panel-3                   # scope to a subtree
```

MCP: tool `snapshot`, arguments `target`, `maxDepth` (default `12`), `all` (default `false`).

Handles are minted here and scoped to the process that minted them — **do not construct or guess
them**, and re-run `snapshot` after the UI changes. The output tags each element with the automation
patterns it supports (e.g. what `invoke` or `fill` will work on) and marks disabled elements.

Handle lifetime differs between the two surfaces — see
[Element handles are process-scoped](Usage.md#concepts-you-need-first). From the CLI, use
[`input --snapshot <target>`](input.md) to snapshot and act in a single process.

`snapshot --vision` deliberately does nothing but point you at [`capture`](capture.md) — asking the tree command
for pixels is answered, not silently served the wrong shape.
