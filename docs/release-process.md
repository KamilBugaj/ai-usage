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

The full pipeline takes roughly 10–15 minutes (parallel matrix builds: Windows + macOS).

## Verifying the release

Check that the public repo release contains all expected files:
- `KB.AI.Usage-Setup.exe`
- `KB.AI.Usage-osx-arm64.zip`
- `KB.AI.Usage-osx-x64.zip`

## Local build (without CI)

Same commands as CI - useful for smoke-testing before pushing a tag:

```bash
dotnet publish src/AiUsage.App/AiUsage.App.csproj \
  -c Release -r win-x64 --self-contained true \
  -p:PublishSingleFile=true \
  -p:IncludeNativeLibrariesForSelfExtract=false \
  -p:PublishTrimmed=true \
  -p:Version=0.1.0 \
  -o publish/win-x64
```

These are the Windows flags. macOS/Linux use
`-p:IncludeNativeLibrariesForSelfExtract=true -p:PublishTrimmed=false` (see
`docs/ci-architecture.md` → Publish flags). Replace `-r win-x64` with the target
RID. Output lands in `publish/<RID>/`.

## Rotating RELEASES_REPO_TOKEN

Current token: rotated 2026-08-03, **expires 2026-12-31**. Keep that line current
when rotating (step 3) and read the real date off the token itself at
https://github.com/settings/tokens?type=beta rather than assuming a fixed lifetime.
The previous entry claimed a 90-day lifetime for a token that lapsed after 31 days,
and the resulting outage went unnoticed for a week.

**Symptom of expiry:** the daily `Prune release assets` run goes red with
`non-200 OK status code: 401 Unauthorized ... "Bad credentials"`. That workflow is
the early warning, and the only one that fails quietly, so a lapsed token can sit
unnoticed for weeks. The same secret also gates the README push and
`action-gh-release` in `release.yml`, so an expired token breaks a release halfway
through, after the builds have already run.

1. Go to https://github.com/settings/tokens?type=beta
2. Renew the token, or generate a new one: resource owner `KamilBugaj`, repository
   access limited to `ai-usage-app-releases`, permission `Contents: Read and write`.
3. Update the secret, then update the "Current token" line above with the new dates:
   ```
   gh secret set RELEASES_REPO_TOKEN --repo KamilBugaj/ai-usage
   ```
   (or github.com → ai-usage → Settings → Secrets and variables → Actions →
   `RELEASES_REPO_TOKEN` → Update secret)
4. Verify without waiting for the cron:
   ```
   gh workflow run prune-releases.yml --repo KamilBugaj/ai-usage
   ```

## Rolling back a bad release

Delete the release manually on the public repo (Releases → Delete), then delete the tag:
```
git push origin --delete v0.1.0
```
Fix, re-tag, and push again.
