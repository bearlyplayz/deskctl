# Building deskctl

## Prerequisites

| Need | Why |
|---|---|
| Windows 10 1903+ | `Windows.Graphics.Capture` — to run what you built |
| .NET 10 SDK, 10.0.302 or later | `global.json` pins the feature band and rolls forward within it |

Check the SDK:

```powershell
dotnet --version    # 10.0.3xx
```

If this reports a lower version or errors with a `global.json` message, install the .NET 10 SDK.
`rollForward: latestFeature` accepts any 10.0.3xx and above; it will not silently fall back to an
older band.

Nothing else is needed. A clone plus the SDK builds and tests the whole solution.

## Build and test

```powershell
dotnet build
dotnet test
```

`TreatWarningsAsErrors` is on for every project, so a warning fails the build — including the
trim/AOT analyzer warnings that `IsAotCompatible` turns on everywhere. That is deliberate: it
surfaces an AOT incompatibility here, at `build` time, rather than leaving it for whoever produces
the release binary.

Tests are xunit.v3 on Microsoft Testing Platform. The test binary is its own runner — there is no
VSTest adapter — so the filter syntax differs from what you may be used to:

```powershell
dotnet test tests/Deskctl.Core.Tests --filter-method '*Translate*'
dotnet test tests/Deskctl.Core.Tests --filter-class '*ScreenCoordsTests'
```

Only `Deskctl.Core` has tests. `Deskctl.Platform` is untested by design — it is P/Invoke and WinRT
against a live desktop, and `deskctl doctor` is its runtime check.

## Run what you built

```powershell
dotnet run --project src/Deskctl -- doctor
dotnet run --project src/Deskctl -- windows list
```

`doctor` is the fastest confirmation that a build works against your machine: it measures display
topology, DPI, and drag thresholds rather than assuming them.

## Adding a package

Versions are managed centrally. Add the version to `Directory.Packages.props`:

```xml
<PackageVersion Include="Some.Package" Version="1.2.3" />
```

and reference it from the project **without** a `Version` attribute:

```xml
<PackageReference Include="Some.Package" />
```

`NuGet.config` clears inherited sources and declares nuget.org alone, so restore does not depend on
whatever private feeds a given machine has configured.

## When the build fails

**`error NETSDK1045` / a `global.json` SDK error.** The installed SDK is older than 10.0.302.
Install the .NET 10 SDK; do not lower the pin.

**An `IL2xxx`/`IL3xxx` warning fails the build.** A trim/AOT incompatibility, reported as an error
because warnings are errors. Fix the incompatibility rather than suppressing the warning — a
suppression here becomes a runtime failure in the AOT-published binary, where JIT no longer covers
for it.

**A green build is not proof the MCP server starts.** Every type crossing JSON must be listed in
`Core/Json/DeskctlJsonContext.cs`, in the same commit that introduces it. MCP builds its tool
schemas at startup, so an unlisted type takes the server down entirely — and only in the published
binary, since JIT hides it under `dotnet run`. The compiler will not catch this for you.
