# Tabbed RDP

`mstsc.exe`, but with tabs. A small Windows Remote Desktop client that hosts the **same RDP engine
mstsc uses** (`mstscax.dll`, the Remote Desktop ActiveX control) — one connection per tab.

Single `.exe`, targets .NET Framework 4.8 (already on every Windows 10/11 / Server 2016+ box), nothing to install.

## Features

- **Tabs in the title bar** (like 1Remote / a browser): status dot per tab (green connected, orange connecting,
  red disconnected), close with ×, middle-click or Ctrl+W, drag tabs to reorder, **+** opens a new connection.
  Drag the empty strip to move the window; snapping and double-click-to-maximize work as usual.
  (Prefer the normal Windows title bar? Alt → View → *Use the standard Windows title bar*.)
- **Hidden menu — tap Alt** (also while a remote session has the keyboard): Connection (new, open .rdp, saved, recent,
  reconnect, disconnect, full screen, duplicate, close), View (sidebar, theme, smart sizing, title bar), Options, Help.
  The app icon at the top-left opens the same menu.
- **Options** — every setting the RDP engine supports, in one dialog with pages:
  *Connection* (computer, user, password), *Display* (fit-to-window / fixed size presets / full screen, smart sizing,
  colour depth, connection bar), *Local resources* (audio playback, microphone, Windows key combinations, clipboard,
  printers, drives, smart cards, COM ports), *Experience* (connection-speed presets, wallpaper, font smoothing,
  composition, animations, themes, auto-reconnect), *Security & advanced* (NLA, server authentication, always prompt,
  /admin, RD Gateway, start program).
  *Options for this connection* → **Apply & reconnect** changes the open tab; **Save as default** makes the settings the
  defaults for every new connection (the **+** dialog, `/v:` launches and anything an .rdp file doesn't specify).
- **Dark / light theme** following the Windows app theme, or forced via Alt → View → Theme.
- **Saved connections sidebar** (Ctrl+B) grouped by client, with search (Ctrl+F), "Connect all" on a group,
  import of `.rdp` files. Passwords are stored DPAPI-encrypted, only when "Remember password" is ticked.

## Using it as an mstsc.exe replacement (web / agent connectors)

Point your connector at `TabbedRDP.exe` instead of `mstsc.exe` — it accepts the same arguments:

```
TabbedRDP.exe connection.rdp
TabbedRDP.exe /v:server01:3390 /w:1366 /h:768 /admin
TabbedRDP.exe connection.rdp /f
```

- **One window:** if Tabbed RDP is already running, a new launch opens as a **new tab** in it (add `/newwindow` to force a separate window).
- **.rdp settings honoured:** full address / server port, username / domain, `password 51:b:` (the DPAPI-encrypted password
  mstsc and most tools write), screen mode (window / full screen), desktopwidth / desktopheight, dynamic resolution,
  smart sizing, session bpp, administrative session, audiomode, audiocapturemode, redirectclipboard / printers /
  smartcards / comports, drivestoredirect, keyboardhook, enablecredsspsupport, authentication level,
  prompt for credentials, alternate shell, shell working directory, connection bar, autoreconnection,
  the "disable wallpaper / themes / …" experience flags, loadbalanceinfo and RD Gateway (gatewayhostname, gatewayusagemethod,
  gatewaycredentialssource, promptcredentialonce).
- **Switches:** `/v: /w: /h: /f /admin /prompt /g:` (plus non-mstsc `/u:` and `/p:` for connectors that can't write files).
  Command-line switches override the file, like mstsc.
- **Saved credentials:** if no password is supplied, `TERMSRV/<host>` entries from Windows Credential Manager are used
  (`cmdkey /generic:TERMSRV/host /user:... /pass:...`). Otherwise the standard Windows credential prompt appears.
- **Handed to the real mstsc.exe** (the embedded control can't do these): `/shadow` (+ `/control /noConsentPrompt`),
  `/multimon`, `/span`, `use multimon:i:1`, `span monitors:i:1`, `/restrictedAdmin`, `/remoteGuard`, RemoteApp files.
- **Troubleshooting:** every launch is logged (passwords masked) to `%APPDATA%\TabbedRDP\launch.log` —
  it shows the exact arguments and .rdp values received and what was opened.

## Keyboard shortcuts

| Keys | Action |
| --- | --- |
| Alt (tap) | Show / hide the menu |
| Ctrl+T / Ctrl+N | New connection |
| Ctrl+O | Open `.rdp` file |
| Ctrl+Tab / Ctrl+Shift+Tab | Next / previous tab |
| Ctrl+W | Close tab (disconnects first) |
| F5 | Reconnect |
| F11 | Full screen (Ctrl+Alt+Break inside a session) |
| Ctrl+B / Ctrl+F | Connections sidebar / search |

While a remote session has the keyboard, keystrokes go to the remote machine (like mstsc) — only the Alt tap is
caught. Click the tab strip first to use the other shortcuts.

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
