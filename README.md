<div align="center">

# SonarHotkeys

**Switch SteelSeries Sonar presets and devices for every channel with global hotkeys.**

[![Latest release](https://img.shields.io/github/v/release/Ronnybest/SonarHotkeys?label=release)](https://github.com/Ronnybest/SonarHotkeys/releases/latest) [![Downloads](https://img.shields.io/github/downloads/Ronnybest/SonarHotkeys/total)](https://github.com/Ronnybest/SonarHotkeys/releases) [![License: MIT](https://img.shields.io/github/license/Ronnybest/SonarHotkeys)](LICENSE.txt) ![Platform](https://img.shields.io/badge/platform-Windows%20x64-0078D4) ![.NET](https://img.shields.io/badge/.NET-10-512BD4)

[**Download**](https://github.com/Ronnybest/SonarHotkeys/releases/latest) · [Changelog](CHANGELOG.md) · [Русский](README.ru.md)

<img src="docs/screenshot.png" alt="SonarHotkeys window with the Game channel and three presets" width="820">

</div>

## Why

Changing a Sonar preset, or the device a channel plays on, usually means opening GG and clicking through Sonar. SonarHotkeys sits in the tray and does it with a keystroke: <kbd>Ctrl</kbd>+<kbd>Alt</kbd>+<kbd>1</kbd> for your game preset on speakers, <kbd>Ctrl</kbd>+<kbd>Alt</kbd>+<kbd>2</kbd> for your FPS preset on headphones, <kbd>Ctrl</kbd>+<kbd>Alt</kbd>+<kbd>M</kbd> for your streaming microphone preset.

## Features

- **Every channel.** Game, Chat, Media, Aux and Mic each have their own presets. A preset can switch a device too: speakers or headphones for the output channels, a microphone for Mic.
- **Favorites appear by themselves.** Presets you mark as favorites in GG show up in their channel on start; your other presets are one click away.
- **Cycle with two shortcuts.** One picks the channel to step through, the other steps through its presets; <kbd>Ctrl</kbd>+<kbd>Alt</kbd>+<kbd>1</kbd>…<kbd>9</kbd> jump to a preset by number.
- **Own shortcuts.** Any preset can have a shortcut of its own that works at any time.
- **Always in sync.** The preset Sonar has selected is marked, even when you change it in GG.
- **Safe switching.** If Sonar rejects part of a change, the previous presets and devices are restored.
- **Tray menu** with every configured preset, and instant notifications.
- **English and Russian interface**; more languages can be added as a single file.
- **Native Windows look.** WinUI 3 with Mica and light and dark themes that follow Windows.
- **Starts with Windows** if you want it to, hidden in the tray.
- **Installer or portable.** A per-user MSI that needs no administrator rights, or a ZIP to run from any folder. Neither needs .NET or the Windows App SDK installed.

## Requirements

- Windows 10 version 1809 or later, or Windows 11; x64
- [SteelSeries GG](https://steelseries.com/gg) with Sonar enabled

## Quick start

1. From the [latest release](https://github.com/Ronnybest/SonarHotkeys/releases/latest), download and run `SonarHotkeys-win-x64.msi`. It installs for your account only, adds SonarHotkeys to the Start menu and starts it when you finish. For a portable copy, download `SonarHotkeys-win-x64.zip` instead, extract the `SonarHotkeys` folder anywhere and run `SonarHotkeys.exe` from it.
2. In GG, mark the presets you want to switch as favorites, in any channel.
3. SonarHotkeys adds them to their channels by itself. Click ↻ at the top left after changing favorites in GG, or **Add** for a preset that is not a favorite.
4. On a preset's card, choose a device, or keep **Keep current device** to switch only the preset.
5. That is it: press <kbd>Ctrl</kbd>+<kbd>Alt</kbd>+<kbd>F12</kbd> to step through the Game presets, or <kbd>Ctrl</kbd>+<kbd>Alt</kbd>+<kbd>1</kbd>…<kbd>9</kbd> to pick one. Changes are saved as you make them, and the shortcuts work with the window closed.

The ✓ button on a card applies its preset right away; ═ drags the card to another place. The **Help** page in the app explains the rest.

## Shortcuts

| Shortcut | Effect |
| --- | --- |
| <kbd>Ctrl</kbd>+<kbd>Alt</kbd>+<kbd>F11</kbd> | Changes the cycled channel, marked ⇄, to the next channel with presets. The sound does not change. |
| <kbd>Ctrl</kbd>+<kbd>Alt</kbd>+<kbd>F12</kbd> | Applies the next preset of the cycled channel, after the one selected in GG now. |
| <kbd>Ctrl</kbd>+<kbd>Alt</kbd>+<kbd>1</kbd>…<kbd>9</kbd> | Applies the preset at that place in the cycled channel's list. Dragging a card renumbers the list. |
| A preset's own shortcut | Applies that preset at any time, whichever channel is cycled. Click the field on the card and press a combination with <kbd>Ctrl</kbd>, <kbd>Alt</kbd> or <kbd>Shift</kbd>; <kbd>Delete</kbd> clears it. |

The cycling shortcuts can be changed or cleared on the **Settings** page, and the number shortcuts turned off there if another application needs them. The cycled channel changes only with its shortcut or the button on a channel page; applying a preset any other way leaves it as it is.

## How switching works

| Sonar mode | What a preset's device changes |
| --- | --- |
| Classic | Output channels: the output device of **Game, Chat, Media** and **Aux**, or of the preset's channel only when **Switch all sound** is off. Mic: the microphone. |
| Streamer | Output channels: the output device of the **Personal** mix. Mic: the microphone. |

The preset itself is always selected in its channel. SonarHotkeys changes Sonar's routing only; the Windows default output device stays the same, so applications must play through Sonar's virtual devices.

## Tray and startup

Closing the window hides it to the tray. Click the tray icon or choose **Settings** to reopen it, and **Exit** to quit. The tray menu lists the presets of every channel, with the cycled channel and the selected presets checked. Starting the app again brings up the window of the running instance.

| Argument | Effect |
| --- | --- |
| `--tray` | Start hidden in the tray. Ignored until at least one preset is configured, and in Debug builds. |

Turn on **Start with Windows** on the **Settings** page or in the tray menu to start SonarHotkeys hidden in the tray when you sign in. It adds a shortcut to your Startup folder, which also appears on the Startup apps page of Task Manager. The portable copy updates the shortcut when you move its folder, and uninstalling the MSI removes it.

## Settings

Settings are stored per user in `%LOCALAPPDATA%\SonarHotkeys\settings.json`, separately from the app, so updating or uninstalling keeps your presets. Version 3.0 converts settings of 1.x and 2.x: their bindings become Game presets with the same devices, after which version 2.x can no longer read them. Preset and device IDs are specific to each computer, so set them up again on a new machine.

To update the installed app, run the new MSI: it closes the running app, replaces it and starts it again. To update the portable copy, exit from the tray and replace the `SonarHotkeys` folder.

A damaged settings file is never overwritten automatically: the app starts with an empty configuration and shows the error. To reset, exit the app and delete the file.

## Troubleshooting

| Problem | Solution |
| --- | --- |
| "GG or Sonar is unavailable" | Start GG, enable Sonar, then click ↻ |
| A preset is missing | Mark it as a favorite in GG and click ↻, or add it with **Add** |
| An output device is missing | Connect it, click ↻ and choose it again |
| "The shortcut is in use" | Another application or Windows owns it; choose a different combination |
| The sound does not move | Applications must play through Sonar's virtual devices rather than straight to your speakers |
| No notifications | Check Windows notification settings and Do Not Disturb |
| "Windows protected your PC" when starting the MSI or EXE | The files are not code-signed yet. Choose **More info**, then **Run anyway** |

## Building from source

Requires Windows and the [.NET 10 SDK](https://dotnet.microsoft.com/download/dotnet/10.0). The release archive is compiled with Native AOT, which also needs the MSVC linker: Visual Studio 2026 or its Build Tools with the **Desktop development with C++** workload.

```powershell
dotnet build SonarHotkeys.slnx -c Release
dotnet run --project tests/SonarHotkeys.Checks/SonarHotkeys.Checks.csproj -c Release
powershell -NoProfile -ExecutionPolicy Bypass -File scripts/Publish.ps1
```

The checks use temporary settings files and a scratch Startup folder, and never touch your real settings, startup entries or Sonar routing. `Publish.ps1` produces `artifacts/SonarHotkeys-win-x64.zip` and `artifacts/SonarHotkeys-win-x64.msi`, both with the application folder, documentation and licenses only. The installer is built with [WiX Toolset](https://wixtoolset.org) 5, restored from NuGet during the build. The version comes from `<Version>` in `SonarHotkeys.WinUI/SonarHotkeys.WinUI.csproj`; keep `app.manifest` in step with it.

| Project | Contents |
| --- | --- |
| `SonarHotkeys.Core` | Settings, shortcut parsing, translations and Sonar switching, independent of the UI |
| `SonarHotkeys.WinUI` | The WinUI 3 app: settings window, tray icon and global hotkeys |
| `installer` | The per-user MSI: Start menu shortcut, upgrades and removal of the autostart shortcut |
| `tests/SonarHotkeys.Checks` | Checks for the core library and the translation files |

Debug builds always show the window and accept `--settings=<file>` to use a separate settings file and `--skip-refresh` to keep the cached presets without contacting Sonar. Without `--settings`, a Debug build works on your real settings file.

## Contributing

Issues and pull requests are welcome.

### Translations

Interface texts live in [`SonarHotkeys.Core/Strings`](SonarHotkeys.Core/Strings), one JSON file per language with stable keys such as `Page.Add`. English (`en.json`) is the reference.

To add a language, copy `en.json` to `<code>.json`, where `<code>` is the two-letter language code (for example `de.json`), set `Language.Name` to the language's own name and translate the values. Keep placeholders such as `{0}` in place. The language appears in the language menu automatically; no code changes are needed. When the Windows display language has a catalog, the app starts in it.

The checks fail when code uses a key missing from `en.json`, when `en.json` has an unused key, or when another language lacks a key or changes its placeholders.

## License

[MIT](LICENSE.txt). Built on [SteelSeries-NET-API](https://github.com/DataNext27/SteelSeries-NET-API); see [third-party notices](THIRD-PARTY-NOTICES.md).

SonarHotkeys is an independent project and is not affiliated with or endorsed by SteelSeries.
