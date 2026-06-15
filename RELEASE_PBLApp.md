# Release process — PoE2 Build Lab (PBLApp, Avalonia)

How to cut a public Windows build of the C#/Avalonia app. (The upstream Lua PoB
release flow lives in [`RELEASE.md`](RELEASE.md) — that's GitHub Actions + NSIS
and does **not** apply here.)

Derived from the existing releases (`publish/PoE2BuildLab-win-x64-v0.1.zip`,
`…-v0.1.1.zip`), the publish profile
`PBLApp/Properties/PublishProfiles/win-x64.pubxml`, and Phase 12 of
[`AVALONIA_MIGRATION_PLAN.md`](AVALONIA_MIGRATION_PLAN.md).

Only `PBLApp` ships. `PBLEngine` / `PBLApp.Core` are bundled inside it; `PBLMcp`,
`PBLParity`, `PBLHost`, and the Lua test harness are **not** part of a release.

## Versioning

A release is identified by one version string `MAJOR.MINOR[.PATCH]` (e.g. `0.1`,
`0.1.1`). It must match in three places — bump all three together:

1. **`PBLApp/PBLApp.csproj`** → `<Version>0.1.1</Version>` (stamps the exe).
2. **Archive file name** → `publish/PoE2BuildLab-win-x64-v<version>.zip`.
3. **Git tag** → `v<version>` (annotated), on the release commit.

PATCH (`0.1.1`) for fixes / small features on top of a minor line; MINOR
(`0.2`) for larger feature batches. There's no separate CHANGELOG — the git log
plus the tag annotation is the record.

## Steps

Run from the repo root in PowerShell.

1. **Land all changes and verify the UI.** Working tree clean, build green, and
   `/pbl-verify` shows real screenshots for anything UI-facing. Never cut a
   release off an unverified UI change.

2. **Bump the version** in `PBLApp.csproj` (see above). Commit it.

3. **Close any running client** — it locks the Release DLLs:
   ```powershell
   Get-Process | Where-Object { $_.Name -like "PBLApp*" } | Stop-Process -Force -ErrorAction SilentlyContinue
   ```

4. **Publish** the self-contained single-file build (clean the output first so no
   stale files leak in):
   ```powershell
   Remove-Item -Recurse -Force publish\win-x64 -ErrorAction SilentlyContinue
   dotnet publish PBLApp\PBLApp.csproj -p:PublishProfile=win-x64 -o publish\win-x64
   ```
   The profile is self-contained (no .NET install needed on the target),
   single-file, compressed; `ReadyToRun` and trimming are deliberately off.

5. **Create the archive** — zip the *contents* of `publish\win-x64` at the root
   (no wrapper folder), matching the prior releases:
   ```powershell
   $ver = "0.1.1"
   $src = "publish\win-x64"
   $out = "publish\PoE2BuildLab-win-x64-v$ver.zip"
   Remove-Item $out -ErrorAction SilentlyContinue
   Add-Type -AssemblyName System.IO.Compression.FileSystem
   [System.IO.Compression.ZipFile]::CreateFromDirectory(
       (Resolve-Path $src), $out,
       [System.IO.Compression.CompressionLevel]::Optimal, $false)  # $false = no base dir
   ```
   Do **not** use `Compress-Archive -Path $src` — it nests everything under a
   `win-x64/` folder. `CreateFromDirectory(..., includeBaseDirectory: $false)`
   keeps `PBLApp.exe` at the archive root.

6. **Verify the archive** before publishing it:
   ```powershell
   Add-Type -AssemblyName System.IO.Compression.FileSystem
   $z = [System.IO.Compression.ZipFile]::OpenRead((Resolve-Path "publish\PoE2BuildLab-win-x64-v$ver.zip"))
   "entries:          $($z.Entries.Count)"
   "exe at root:      " + [bool]($z.Entries | Where-Object { $_.FullName -eq 'PBLApp.exe' })
   "TreeData present: " + [bool]($z.Entries | Where-Object { $_.FullName -like 'Assets/TreeData/*' } | Select-Object -First 1)
   $z.Dispose()
   ```
   Expect: `PBLApp.exe` at root = True, `Assets/TreeData/*` present = True,
   several thousand entries. Smoke-test by extracting to a clean folder and
   launching `PBLApp.exe` (without `--enable-ipc`).

7. **Tag the release:**
   ```powershell
   git tag -a v$ver -m "PoE2 Build Lab v$ver"
   ```
   Push the tag only when the user asks to publish.

## What's in the archive

| Path                      | What                                                   |
|---                        |---                                                     |
| `PBLApp.exe`              | Single-file self-contained app (~58 MB)                |
| `Assets/TreeData/<ver>/`  | Passive-tree sprite sheets (WebP, copied as Content)   |
| `ItemIcons/`              | Item / gem icon sprites                                |
| `src/`                    | Lua calc engine sources (`CopyEngineFiles` target)     |
| `runtime/lua/`, `lua/`    | Lua 5.4 compat shims                                   |

Reference sizes (v0.1.1): publish output ≈ 169 MB on disk, zipped ≈ 121 MB,
~4900 entries.

## `publish/` is not committed

`publish/` is git-ignored — the `.zip` and `win-x64/` output are build
artifacts, not source. Distribute the archive out-of-band (release upload);
only the version bump, code, and tag go into git.
