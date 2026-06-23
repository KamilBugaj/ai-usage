# CI Architecture

## Workflows

### `ci.yml` — pull requests & pushes to main

Triggers on every PR and push to `main`. Steps:
- `dotnet restore` + `dotnet build -c Release`
- `dotnet test -c Release`

Matrix: `windows-latest`, `macos-latest`, `ubuntu-latest`. No packaging.

### `release.yml` — push of a `v*` tag

Two stages:

**Job `build`** (4-entry matrix):

| Runner         | RID         | Artifacts                                            |
|----------------|-------------|------------------------------------------------------|
| windows-latest | win-x64     | `KB.AI.Usage-win-x64.zip`, `KB.AI.Usage-Setup.exe`  |
| macos-latest   | osx-arm64   | `KB.AI.Usage-osx-arm64.zip`                          |
| macos-latest   | osx-x64     | `KB.AI.Usage-osx-x64.zip`                            |
| ubuntu-latest  | linux-x64   | `KB.AI.Usage-linux-x64.tar.gz`                       |

**Job `publish`** (needs: build):
- Downloads all artifacts
- Creates a GitHub Release in the public repo via `softprops/action-gh-release@v2`
- Token: `RELEASES_REPO_TOKEN` (secret in the private repo)

## Cross-repo push

The private CI pushes a Release to `KamilBugaj/ai-usage-app-releases` using a
fine-grained PAT with `Contents: Read and write` permission scoped to that repo only.

Secret: `RELEASES_REPO_TOKEN` in `kb-ai-usage`.

The PAT expires every 90 days — see `docs/release-process.md` → Rotating the token.

## Publish flags

```
--self-contained true          user has no .NET 10 runtime installed
-p:PublishSingleFile=true      single executable output
-p:IncludeNativeLibrariesForSelfExtract=true
-p:PublishTrimmed=false        Avalonia uses reflection — trimming breaks XAML bindings
-p:Version=$VERSION            derived from tag (v0.1.0 → 0.1.0)
```

## Packaging

- **Windows zip**: `Compress-Archive` (PowerShell)
- **Windows installer**: Inno Setup (`choco install innosetup` on the runner),
  script at `packaging/windows/installer.iss`, env vars: `APP_VERSION`, `PUBLISH_DIR`, `DIST_DIR`
- **macOS**: `packaging/macos/make-app.sh` — assembles the `.app` bundle,
  packs with `ditto -c -k --keepParent` (preserves executable bit)
- **Linux**: `chmod +x` + `tar czf`

## Triggering

```bash
# Cut a release
git tag v0.1.0
git push origin v0.1.0
```

Tags containing `-` (e.g. `-beta`, `-rc1`) are automatically marked as pre-release.

## Secrets

| Secret               | Where set              | Notes                                     |
|----------------------|------------------------|-------------------------------------------|
| `RELEASES_REPO_TOKEN`| kb-ai-usage → Settings | Fine-grained PAT, expires every 90 days   |
