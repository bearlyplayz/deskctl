# Releasing deskctl

This describes how a release is produced. It requires push access to the deskctl repository and,
for the winget half, ownership of the account that publishes the package — if you are working from
a fork or a clone, none of this applies to you, and `docs/Build.md` is what you want.

## Cutting a release

The tag is the version. Nothing else records it: there is no version in any `.csproj`, deliberately,
so the two can never disagree.

```powershell
git tag v1.2.3
git push origin v1.2.3
```

`.github/workflows/release.yml` then runs the tests, publishes the NativeAOT binary with
`-p:Version=1.2.3`, and creates a GitHub release with notes generated from the commits since the
previous tag. The single asset is `deskctl.exe`.

The tag must be `vMAJOR.MINOR.PATCH`. The workflow rejects anything else rather than stamping a
version that `deskctl --version` would report incorrectly.

## Publishing to winget

The release workflow submits each new version to
[microsoft/winget-pkgs](https://github.com/microsoft/winget-pkgs) with
[`wingetcreate`](https://github.com/microsoft/winget-create), Microsoft's own manifest tool. It can
only do so once the package exists there: `wingetcreate update` edits an existing package and will
not create one, so the first version is submitted by hand.

### One-time setup

Nobody has write access to `microsoft/winget-pkgs`, so every submission is a pull request from a
fork of it owned by the submitter. There is no need to create that fork by hand — `wingetcreate`
looks for it and forks the repository itself when it is absent, so the first submission below
brings it into existence as a side effect.

1. **Create a classic Personal Access Token** with the `public_repo` scope, and add it to this
   repository as the secret `WINGET_TOKEN`. Fine-grained tokens do not work. The built-in
   `GITHUB_TOKEN` cannot be used either: it cannot push to a fork of a repository owned by someone
   else.

2. **Submit the first version by hand.** Cut a normal release first, then point `wingetcreate` at
   its asset:

   ```powershell
   winget install Microsoft.WingetCreate
   wingetcreate token          # stores the PAT from step 1 locally
   wingetcreate new https://github.com/bearlyplayz/deskctl/releases/download/v1.2.3/deskctl.exe
   ```

   It computes the installer hash and prompts for the manifest fields. The values that matter:

   | Field | Value |
   |---|---|
   | `PackageIdentifier` | `deskctl.deskctl` |
   | `InstallerType` | `portable` |
   | `Commands` | `deskctl` |
   | `License` | `MIT` |

   `PackageIdentifier` is case sensitive — it maps to the `manifests/d/deskctl/deskctl` path in
   `winget-pkgs`. The `winget` job passes it to `wingetcreate update`, which finds nothing if the
   case differs, so the two have to be changed together or not at all.

   `Commands` is what users type. Without it winget derives the alias from the asset filename,
   which is `deskctl.exe` and so happens to give the right answer today — setting it explicitly is
   what keeps that true if the asset is ever renamed. It is set once here and carried forward onto
   every later version automatically.

   `wingetcreate` offers to open the PR for you. A first-time contributor signs Microsoft's CLA
   (a bot comments on the PR explaining how), and a human moderator reviews it. Expect this to
   take days rather than minutes, and expect more scrutiny than later versions get.

3. **Once that PR is merged**, set the repository variable `PUBLISH_WINGET` to `true`. That is what
   enables the `winget` job; until it is set, the job is skipped and releases stay green. Every
   subsequent tag then submits its own update PR with no further work.

### What to expect afterwards

The `winget` job downloads `wingetcreate` and runs the equivalent of:

```powershell
wingetcreate update deskctl.deskctl --version 1.2.3 `
  --urls 'https://github.com/bearlyplayz/deskctl/releases/download/v1.2.3/deskctl.exe' `
  --submit --no-open --token <WINGET_TOKEN>
```

It rehashes the installer from that URL and opens a PR, which is usually merged by automated
validation without a human involved. The binary is unsigned, which winget permits, but SmartScreen scores an
unsigned executable's reputation from zero on every new version, so a release can occasionally be
held for a moderator. That is a delay, not a rejection.
