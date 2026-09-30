# SonarHotkeys

[Документация на русском](README.md)

A Windows tray application for switching SteelSeries Sonar Game presets and output devices with global shortcuts or the tray menu. Configure your own presets, devices and shortcuts without rebuilding the application.

## Download and run

1. Download `SonarHotkeys-win-x64.zip` from the [latest release](https://github.com/Ronnybest/SonarHotkeys/releases/latest).
2. Extract the archive and run `SonarHotkeys.exe`.
3. Install and start SteelSeries GG, then enable Sonar.

The Windows x64 release includes .NET. You do not need to install .NET or Visual Studio separately. SonarHotkeys uses GG's local API and is not affiliated with SteelSeries.

## First-time setup

Use **Язык / Language** at the top of the window to select **English** or **Русский**. The window, tray menu and messages update immediately. The preference is saved without committing unfinished preset bindings. A fresh installation defaults to Russian on Russian-language Windows and English otherwise.

1. Favorite your desired **Game** presets in GG.
2. Click **Refresh from Sonar** to discover your presets and output devices.
3. Click **Add** for each binding and choose a preset.
4. Choose a physical output device, or leave **Keep current device** to change only the preset.
5. Double-click the shortcut cell and press a combination with Ctrl, Alt or Shift, such as Ctrl+Alt+1. Delete or Backspace clears it.
6. Optionally assign a separate shortcut in **Cycle configured presets** below the table.
7. Click **Save** to activate the new bindings and shortcuts.

No shortcuts or presets are assigned on a fresh installation. Each preset and shortcut must be unique. **Apply selected** tries a binding before saving it. Bindings without shortcuts remain available through the tray menu and cycling.

## Audio switching

- **Classic** mode changes the output device for **Game, Chat, Media and Aux**.
- **Streamer** mode changes the **Personal** output device.
- Only the **Game** preset changes.
- Windows' default output device is unchanged. Applications must play through Sonar's virtual channels.
- If a change fails partway through, the application attempts to restore the previous settings and reports the result.

Cycling follows the table's row order and reads the actual preset selected in GG. Missing presets and presets removed from favorites are skipped. The tray menu lists configured favorites and applies their saved output devices.

## Tray and notifications

Closing the window hides it to the tray. Double-click the icon or choose **Settings** to reopen it. Choose **Exit** to quit. Starting the application again opens the window of the running instance instead of launching a second copy.

The `--tray` argument starts a configured application with its window hidden. First-time setup remains visible when no bindings have been saved. Debug builds always show the window and ignore the argument.

Notifications show the selected preset and output, or an error. Click a notification to open the full message. Windows notification settings and Do Not Disturb can suppress notifications.

## Settings and updates

Settings are stored per user in `%LOCALAPPDATA%\SonarHotkeys\settings.json`, separately from the executable. Preset and device identifiers are specific to your installation. Your settings are not included in release archives.

To update, exit from the tray and replace the executable. Saved bindings and language preferences remain. See the [changelog](CHANGELOG.md) for what changed in each version. On another computer, select its presets and devices again because identifiers may differ.

If the settings file is damaged, the application opens an empty configuration and reports the error. It does not automatically overwrite the damaged file. To reset, exit the application and rename or delete the settings file.

## Troubleshooting

| Problem | Action |
| --- | --- |
| GG or Sonar is unavailable | Start GG, enable Sonar and refresh |
| No presets appear | Favorite Game presets in GG |
| A device is unavailable | Connect it, refresh and choose an available output |
| A shortcut is in use | Choose another shortcut; failed saves keep previous bindings active |
| A preset was deleted or replaced | Refresh and select it again |

## Build from source

Windows and .NET 10 SDK are required. From the repository root:

```powershell
dotnet build SonarHotkeys.slnx -c Release
dotnet run --project tests/SonarHotkeys.Checks/SonarHotkeys.Checks.csproj -c Release
```

The checks use temporary settings files and never touch your real settings or Sonar audio routes. They also verify that every interface text has an English translation.

Build the self-contained release archive:

```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File scripts/Publish.ps1
```

The result is `artifacts/SonarHotkeys-win-x64.zip`. It contains the executable, Russian and English instructions, the changelog and license notices. Local user settings are never copied into the release. Attach the archive to a GitHub release.

## Translations

Interface texts are written in Russian in the source code, and the Russian text serves as the translation key. English translations live in `SonarHotkeys/Translations.json`, which is embedded into the executable. When you add or change a text, update its entry in that file; the checks fail if an entry is missing or no longer used.

Project license: [MIT](LICENSE.txt). Library: [SteelSeries-NET-API](https://github.com/DataNext27/SteelSeries-NET-API). See [third-party notices](THIRD-PARTY-NOTICES.md).
