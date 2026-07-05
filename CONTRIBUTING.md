# Contributing

Thanks for your interest in KB.AI.Usage.

## Reporting bugs and requesting features

Open an [issue](../../issues). For bugs, include your app version, OS, and which
provider tile is affected — the issue form asks for these.

## Development setup

See [docs/build-from-source.md](./docs/build-from-source.md). In short: .NET 10 SDK, then

```bash
dotnet build KB.AI.Usage.slnx -c Release
dotnet test KB.AI.Usage.slnx
```

Architecture overview: [ARCHITECTURE.md](./ARCHITECTURE.md).

## Pull requests

- For anything larger than a small fix, open an issue first so the approach can be
  agreed before you invest time.
- Keep a PR focused on a single change.
- Make sure `dotnet build` and `dotnet test` pass.
- Commit messages: English, imperative mood, subject line ≤ 72 characters.

## License

By contributing you agree that your contributions are licensed under the
[MIT License](./LICENSE).
