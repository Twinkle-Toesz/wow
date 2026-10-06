# Tabbed RDP

`mstsc.exe`, but with tabs. A small Windows Remote Desktop client that hosts the **same RDP engine
mstsc uses** (`mstscax.dll`, the Remote Desktop ActiveX control) — one connection per tab.

Single `.exe`, targets .NET Framework 4.8 (already on every Windows 10/11 / Server 2016+ box), nothing to install.

## Features

- **Tabs** — every connection opens in its own tab, with a status dot (orange = connecting, green = connected, red = disconnected).
  Close with the ×, middle-click, or Ctrl+W. Right-click a tab for Reconnect / Disconnect / Full screen / Smart sizing / Duplicate / Save.
- **Quick connect bar** like mstsc's "Computer:" box — type `server`, `server:3390` or `[fe80::1]:3389` and hit Enter.
  Saved connections are matched automatically; otherwise you get a credentials prompt.
- **Saved connections sidebar** grouped by client/group, with search (Ctrl+F), double-click to connect,
  "Connect all" on a group, duplicate, import `.rdp` files.
- **Auto-resize** — "Fit to window" resizes the remote desktop when you resize the window/tab (Win 8.1 / 2012 R2+ hosts),
  or use smart sizing / a fixed resolution.
- **Full screen** (F11) with the normal RDP connection bar; Windows-key combos go to the remote machine while full screen.
- mstsc options: `/admin` session, NLA/CredSSP, colour depth, clipboard / drive / printer / smart-card redirection, audio mode,
  auto-reconnect.
- Passwords are stored encrypted with **DPAPI** (only your Windows account on that machine can decrypt them), and only if you tick "Remember password".
- Opens `.rdp` files and accepts `mstsc`-style arguments: `TabbedRDP.exe /v:server01:3390 other.rdp`.

## Keyboard shortcuts

| Keys | Action |
| --- | --- |
| Ctrl+N | New connection (all options) |
| Ctrl+L | Focus the computer box |
| Ctrl+O | Open `.rdp` file |
| Ctrl+F | Search saved connections |
| Ctrl+Tab / Ctrl+Shift+Tab | Next / previous tab |
| Ctrl+W | Close tab (disconnects first) |
| F5 | Reconnect |
| F11 | Full screen |

While a remote session has keyboard focus, keystrokes go to the remote machine (like mstsc) — click the tab strip,
toolbar or sidebar first to use the shortcuts. Ctrl+Alt+Break toggles full screen from inside a session.

## Download / build

- **Download:** every push builds on GitHub Actions — open the latest *Build* run and grab the `TabbedRDP` artifact.
- **Build yourself** (any machine with the .NET 8 SDK; the output is a .NET Framework 4.8 exe):

  ```
  dotnet build src/TabbedRdp/TabbedRdp.csproj -c Release -o publish
  publish\TabbedRDP.exe
  ```

Settings and saved connections live in `%APPDATA%\TabbedRDP\settings.json`.

## How it works

`RdpClientHost` wraps the RDP ActiveX control in a WinForms `AxHost` with no generated interop assemblies:
it reads `mstscax.dll`'s type library at startup and picks the newest registered `MsRdpClient<N>NotSafeForScripting`
class, then drives it late-bound. Events (connected / login complete / disconnected) come in through a small
connection-point sink.
