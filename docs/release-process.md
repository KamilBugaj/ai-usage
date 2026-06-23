# Release Process

## Cutting a release

1. Make sure `main` is green (CI passes).
2. Update `RELEASE_NOTES.md` with a description of the changes.
3. Tag and push:
   ```
   git tag v0.1.0
   git push origin v0.1.0
   ```
4. GitHub Actions runs `release.yml` automatically.
   Monitor progress: Actions → Release → current run.
5. After success, verify the release on the public repo:
   https://github.com/KamilBugaj/ai-usage-app-releases/releases

The full pipeline takes roughly 10–15 minutes (parallel matrix builds on 3 OSes).

## Verifying the release

Check that the public repo release contains all expected files:
- `KB.AI.Usage-win-x64.zip`
- `KB.AI.Usage-Setup.exe`
- `KB.AI.Usage-osx-arm64.zip`
- `KB.AI.Usage-osx-x64.zip`
- `KB.AI.Usage-linux-x64.tar.gz`

## Local build (without CI)

Same commands as CI — useful for smoke-testing before pushing a tag:

```bash
dotnet publish src/AiUsage.App/AiUsage.App.csproj \
  -c Release -r win-x64 --self-contained true \
  -p:PublishSingleFile=true \
  -p:IncludeNativeLibrariesForSelfExtract=true \
  -p:PublishTrimmed=false \
  -p:Version=0.1.0 \
  -o publish/win-x64
```

Replace `-r win-x64` with the target RID. Output lands in `publish/<RID>/`.

## Rotating RELEASES_REPO_TOKEN

The PAT expires every 90 days.

1. Go to https://github.com/settings/tokens?type=beta
2. Renew the token (or generate a new one with the same permissions).
3. Update the secret in the source repo:
   github.com → ai-usage → Settings → Secrets and variables → Actions →
   `RELEASES_REPO_TOKEN` → Update secret.

## Rolling back a bad release

Delete the release manually on the public repo (Releases → Delete), then delete the tag:
```
git push origin --delete v0.1.0
```
Fix, re-tag, and push again.
