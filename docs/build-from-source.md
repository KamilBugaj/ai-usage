# Build from Source

## Prerequisites

- [.NET 10 SDK](https://dotnet.microsoft.com/download/dotnet/10.0)
- Windows: no additional dependencies (tray works natively)
- macOS: no additional dependencies (tray via NSStatusItem)
- Linux: `libappindicator3-1` or `libayatana-appindicator3-1` depending on distro
  ```bash
  # Ubuntu/Debian
  sudo apt install libappindicator3-1
  # Fedora
  sudo dnf install libappindicator-gtk3
  ```

## Build

```bash
dotnet build KB.AI.Usage.slnx -c Release
```

## Test

```bash
dotnet test KB.AI.Usage.slnx -c Release --logger "console;verbosity=normal"
```

## Run (development)

```bash
dotnet run --project src/AiUsage.App/AiUsage.App.csproj
```

Config file location:
- Windows: `%APPDATA%\AiUsage\config.json`
- macOS/Linux: `~/.config/AiUsage/config.json`

See `config/config.example.json` for the expected structure.

## Publish (locally)

```bash
dotnet publish src/AiUsage.App/AiUsage.App.csproj \
  -c Release -r win-x64 --self-contained true \
  -p:PublishSingleFile=true \
  -p:IncludeNativeLibrariesForSelfExtract=true \
  -p:PublishTrimmed=false \
  -p:Version=0.0.0-local \
  -o publish/win-x64
```

Replace `-r win-x64` with the target RID (`osx-arm64`, `osx-x64`, `linux-x64`).

## Regenerating icon files

Requires Python 3 + Pillow:
```bash
pip install pillow
python packaging/icon/generate.py
```

Outputs `master.png`, `app.ico`, `icon.icns`, `icon-256.png`, `icon-512.png`
into `packaging/icon/`. Copy `app.ico` to `src/AiUsage.App/Assets/` afterwards.
