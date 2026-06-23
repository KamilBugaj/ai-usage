## What's new

- **GitHub Copilot via device flow** — Connect signs in through GitHub (browser authorize)
  and reads your real premium-request quota from the Copilot usage API, replacing the old
  VS Code log scraping.
- **Connect / Disconnect** for Claude.ai and ChatGPT, all from the in-app Settings panel.
- **ChatGPT free accounts** now show a clear "needs a paid plan (Plus / Pro / Codex)"
  message instead of an error — free tier has no usage API to read.
- **Tri-coloured usage bars** (green → yellow near the limit → red at/over it) and a
  mandatory alert threshold (default 80%).
- **Provider logos** on each tile; reset shown as a filling ring with a hover tooltip.
- Removed the OpenAI API and Anthropic API tiles for now.
- Fixes: reset ring resets correctly after a window rolls over; the silent session-restore
  window no longer flashes on startup.

## Installation

See the [README](https://github.com/KamilBugaj/ai-usage-app-releases#readme) for
platform-specific installation instructions and workarounds for SmartScreen / Gatekeeper.
