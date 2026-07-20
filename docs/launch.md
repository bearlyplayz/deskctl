[← Usage index](Usage.md)

# launch — start a program and find its window

Starts a program, redirects its stdout and stderr to a log file, and waits for the window it opens.
It is the step before everything else: the other commands all need an `hwnd`, and this is how you get
one for a program that is not running yet.

```powershell
windeskctl launch notepad.exe
windeskctl launch "C:\Program Files\App\app.exe" -- --profile work    # arguments after --
windeskctl launch app.exe --log C:\logs\app.log --title "Main Window"
windeskctl launch app.exe --wait-ms 0                                 # start it, do not wait
windeskctl launch app.exe --json
```

```
pid 44328  exited 0
log C:\Users\you\AppData\Local\Temp\windeskctl\notepad-20260720-212622-663.log
win:49810644  4124,401 1205x1570  Normal  Notepad  Untitled - Notepad
```

MCP: tool `launch`, arguments `path`, `arguments`, `environment`, `workingDirectory`, `logPath`,
`appendLog`, `waitForWindowMs`, `settleMs`, `titleContains`, `processName`.

## What it returns

| Field | |
|---|---|
| `processId` | The launched process. Always accurate. |
| `logPath` | Where its output is going. Always accurate, and the file exists even if the program writes nothing. |
| `exitCode` | `null` while it is still running. |
| `window` | The window it opened — **best effort**, see below. `null` when none could be attributed to the launch. |
| `otherWindows` | Every other window attributed to the launch, newest last. |

In JSON, **null fields are omitted**, so a missing `window` or `exitCode` key means null.

**The process facts are exact; the window is a heuristic.** That split is the whole design. Treat
`window` as a convenience that saves you a [`windows`](windows.md) call most of the time, not as a
guarantee.

## Arguments are passed literally

Each argument reaches the program exactly as written. No shell interprets the command line, so
`&`, `|`, `^`, `>`, and `%VAR%` are ordinary characters rather than syntax:

```powershell
windeskctl launch app.exe -- "https://example.com/?a=1&b=2"
```

On the CLI, everything after `--` belongs to the launched program. Over MCP, `arguments` is an array
and each element is one argument — do not pre-join them into a string, and do not add quotes of your
own.

## The log

Without `--log`, output goes to `%TEMP%\windeskctl\<program>-<timestamp>.log`. The file is truncated
on each launch unless you pass `--append`.

Both streams share one file, so they interleave in the order the program actually wrote them. The
file can be read while the program is still running.

A GUI program usually writes nothing here — that is normal and not a sign the launch failed. The log
earns its keep when a program **fails** to start, which is exactly when `window` is `null` and you
need to know why.

## Environment

`--env NAME=VALUE` (repeatable; `environment` over MCP) is **layered over** the current environment,
not a replacement for it. Names are matched case-insensitively, so `--env path=...` overrides the
inherited `PATH` rather than producing a second entry.

## How the window is found

A census of every top-level window is taken *before* the program starts. Anything that appears
afterwards is a candidate only if it belongs to the launched process **or one of its descendants**.

Two conditions, not one, because either alone is wrong:

- Newness alone would claim a window from an unrelated program that happened to start during the
  wait — a real risk when the wait is a minute long.
- Lineage alone would claim windows the program already had open.

Together they are exact, including when a launcher spawns a differently-named child to own the real
window.

**The exception is hand-off.** Chrome, Explorer, and other single-instance programs pass the request
to a copy of themselves that is already running and then exit — so the window has no relationship to
the process that was launched, and `exitCode` is `0` almost immediately. Only in that case does
windeskctl fall back to matching the executable name. If a second copy of the same program happens to
start at the same instant, it can be mis-attributed; the alternatives are in `otherWindows`.

### Splash screens

A splash screen is often a genuine visible, titled, top-level window from the same process, so
nothing distinguishes it from the real one in general. Three things narrow it:

1. Windows with a **title bar** outrank bare popups. Most splash screens are captionless.
2. After the first window appears, windeskctl keeps watching for `--settle-ms` (default 1000) and
   prefers the newest survivor, so a splash that is replaced does not win.
3. Everything that lost is returned in `otherWindows`.

**If the program has a splash screen, pass `--title`.** It turns a guess into a match and is the
deterministic answer. It **ranks rather than filters**, so a near miss — a localized title, a version
suffix, a document name prefixed to the app name — still returns a window instead of nothing. The
same is true of `--process`.

Ranking order: title hint, then process hint, then has-a-title-bar, then newest.

When the pick is wrong, take one of `otherWindows` or call [`windows`](windows.md). Do not launch the
program a second time.

## When `window` is null

Not an error, and not a refusal — the command still succeeds. It means one of:

- The program opens no window at all.
- **It reused a window it already had** instead of opening a new one. Windows 11 Notepad is the
  everyday example: a second launch adds a tab to the existing window, and no new window ever
  appears. Nothing is wrong — call [`windows`](windows.md) to get the existing one.
- It was slower than `--wait-ms` (default 60000). Raise it, or call [`windows`](windows.md) later.
- It failed to start. Check `exitCode` and read the log.
- `--wait-ms 0` was passed, which starts the program and returns immediately.

The log path is returned in every one of these cases, which is the point of not refusing.

## Limits

- **Elevated programs cannot be launched.** windeskctl could not automate their windows afterwards
  either — UIPI blocks input to a higher integrity level than its own.
- **A program cannot be told which monitor to open on**; no such argument exists. Launch it, then
  place it with [`windows move`](windows.md).
- **Virtual desktops are not modelled.** A launched program lands on the active desktop.
- The launched program **outlives windeskctl**. There is no stop command; end it the way you
  normally would.
