# Changelog / Журнал изменений

## 1.0.0 — 2026-09-30

First public release. / Первый публичный релиз.

### English

- Switch SteelSeries Sonar **Game** presets together with a physical output device using global shortcuts, the tray menu or a separate shortcut that cycles through configured presets.
- Output switching follows the Sonar mode: **Game, Chat, Media and Aux** in Classic, **Personal** in Streamer. If a change fails partway through, the previous settings are restored.
- Russian and English interface. The language can be switched at any time at the top of the window and applies immediately to the window, tray menu and messages.
- A fresh installation starts with no presets or shortcuts assigned. Presets and devices are discovered from Sonar on the user's own computer.
- The setup window stays visible on first run even with `--tray`. Debug builds always show the window.
- Starting the application again opens the window of the running instance instead of exiting silently.
- Settings are stored in `%LOCALAPPDATA%\SonarHotkeys\settings.json`, saved atomically and never overwritten automatically when damaged.
- Self-contained single-file `SonarHotkeys.exe` for Windows x64: .NET does not need to be installed.

### Русский

- Переключение пресетов **Game** в SteelSeries Sonar вместе с физическим устройством вывода: глобальными сочетаниями клавиш, из меню в трее или отдельным сочетанием для перебора настроенных пресетов.
- Устройство меняется с учётом режима Sonar: **Game, Chat, Media и Aux** в Classic, **Personal** в Streamer. При частичной ошибке предыдущие настройки восстанавливаются.
- Интерфейс на русском и английском. Язык переключается в любой момент вверху окна и сразу применяется к окну, меню в трее и сообщениям.
- При первой установке пресеты и сочетания не назначены. Пресеты и устройства загружаются из Sonar на компьютере пользователя.
- Окно настройки при первом запуске открывается даже с `--tray`. В Debug-сборке окно показывается всегда.
- Повторный запуск открывает окно уже запущенной копии вместо молчаливого выхода.
- Настройки хранятся в `%LOCALAPPDATA%\SonarHotkeys\settings.json`, записываются атомарно, а повреждённый файл не перезаписывается автоматически.
- Самостоятельный `SonarHotkeys.exe` одним файлом для Windows x64: устанавливать .NET не нужно.
