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

The release workflow can submit each new version to
[microsoft/winget-pkgs](https://github.com/microsoft/winget-pkgs) automatically, but only once the
package exists there. The first version has to be submitted by hand, because
`winget-releaser` refuses to create a package that is not already present.

### One-time setup

1. **Fork `microsoft/winget-pkgs`** under the same account that owns this repository. The action
   pushes its branch to that fork and opens a cross-repository PR from it, and it does not create
   the fork for you.

   This requirement is not in Microsoft's manifest documentation, which describes the YAML schema
   rather than how a submission reaches the repository. It comes from
   [winget-pkgs CONTRIBUTING.md](https://github.com/microsoft/winget-pkgs/blob/master/CONTRIBUTING.md)
   ("First, fork the repository to your own GitHub account") and from
   [winget-releaser](https://github.com/vedantmgoyal9/winget-releaser), which requires the fork to
   sit under the release repository's owner unless its `fork-user` input says otherwise. Nobody has
   write access to `microsoft/winget-pkgs`, so every contribution is a PR from a fork.

2. **Create a classic Personal Access Token** with the `public_repo` scope, and add it to this
   repository as the secret `WINGET_TOKEN`. Fine-grained tokens do not work — the action rejects
   them. The built-in `GITHUB_TOKEN` cannot be used either: it cannot push to a fork of a
   repository owned by someone else.

3. **Submit the first version manually.** Cut a normal release first, then point `wingetcreate` at
   its asset:

   ```powershell
   winget install Microsoft.WingetCreate
   wingetcreate new https://github.com/bearlyplayz/deskctl/releases/download/v1.2.3/deskctl.exe
   ```

   It computes the installer hash and prompts for the manifest fields. The values that matter:

   | Field | Value |
   |---|---|
   | `PackageIdentifier` | `Deskctl.Deskctl` |
   | `InstallerType` | `portable` |
   | `Commands` | `deskctl` |
   | `License` | `MIT` |

   `Commands` is what users type. Without it winget derives the alias from the asset filename,
   which is `deskctl.exe` and so happens to give the right answer today — setting it explicitly is
   what keeps that true if the asset is ever renamed. It is set once here and carried forward onto
   every later version automatically.

   `wingetcreate` offers to open the PR for you. A first-time contributor signs Microsoft's CLA
   (a bot comments on the PR explaining how), and a human moderator reviews it. Expect this to
   take days rather than minutes, and expect more scrutiny than later versions get.

4. **Once that PR is merged**, set the repository variable `PUBLISH_WINGET` to `true`. Every
   subsequent tag then submits its own update PR with no further work.

### What to expect afterwards

Each release opens a PR against `winget-pkgs` that is usually merged by automated validation
without a human involved. The binary is unsigned, which winget permits, but SmartScreen scores an
unsigned executable's reputation from zero on every new version, so a release can occasionally be
held for a moderator. That is a delay, not a rejection.
