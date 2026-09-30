# Changelog / Журнал изменений

## 2.1.0 — 2026-10-01

Installer and starting with Windows. / Установщик и запуск вместе с Windows.

### English

- New `SonarHotkeys-win-x64.msi` installer. It installs for the current user into `%LOCALAPPDATA%\Programs\SonarHotkeys` without administrator rights, adds a Start menu shortcut and an entry in Settings → Apps, and can start the app when it finishes. The portable ZIP is still available.
- Installing a new MSI over an older one closes the running app, replaces it and starts it again. Settings are kept.
- **Start with Windows** switch in the window and the tray menu. It adds a shortcut to your Startup folder that starts the app hidden in the tray; a moved portable folder updates it, and uninstalling the MSI removes it.
- The app now exits properly when Windows signs out or shuts down, or an installer asks it to close, instead of only hiding its window.

### Русский

- Новый установщик `SonarHotkeys-win-x64.msi`. Он ставит программу для текущего пользователя в `%LOCALAPPDATA%\Programs\SonarHotkeys` без прав администратора, добавляет ярлык в «Пуск» и запись в «Параметры → Приложения» и может запустить программу по окончании. Портативный ZIP остаётся.
- Установка нового MSI поверх старого закрывает работающую программу, заменяет её и запускает снова. Настройки сохраняются.
- Переключатель **Запускать вместе с Windows** в окне и в меню трея. Он добавляет в папку «Автозагрузка» ярлык, который запускает программу со скрытым окном в трее; при переносе портативной папки ярлык обновляется, а удаление MSI убирает его.
- Программа теперь корректно завершается при выходе из системы, выключении или по просьбе установщика, а не просто прячет окно.

## 2.0.0 — 2026-09-30

New WinUI 3 interface. / Новый интерфейс на WinUI 3.

### English

- The settings window is rebuilt with WinUI 3: Fluent design, Mica background, light and dark themes that follow Windows, and one card per binding instead of a table.
- New application icon in the window, taskbar and tray.
- Changes are saved as you make them; the Save button is gone. Hotkeys pause only while a shortcut field has focus in the active window, so a new shortcut works as soon as you switch to a game.
- The status message floats over the list, so it never moves the content, and hides itself after a few seconds.
- Translations are key-value files, one per language in `SonarHotkeys.Core/Strings`. A new language is a single file and appears in the language menu automatically.
- The language is chosen from a menu that always opens below the button.
- Clicking the tray icon opens the window; the tray menu follows the dark theme.
- The release is a folder in a ZIP archive, compiled with Native AOT: a 25 MB download instead of 44 MB. Extract the `SonarHotkeys` folder and run `SonarHotkeys.exe`; nothing needs to be installed.
- Requires Windows 10 version 1809 or later.
- Settings saved by 1.x are read as they are, including shortcuts such as `Ctrl + Alt + D1`.
- The release now includes the license terms of the redistributed Windows App SDK and WebView2 files in `licenses/`.

### Русский

- Окно настроек переписано на WinUI 3: оформление Fluent, фон Mica, светлая и тёмная темы по настройкам Windows, карточка на каждую привязку вместо таблицы.
- Новый значок приложения в окне, на панели задач и в трее.
- Изменения сохраняются сразу, кнопки «Сохранить» больше нет. Горячие клавиши отключаются, только пока поле сочетания в фокусе в активном окне, поэтому новое сочетание работает сразу после переключения в игру.
- Сообщение в окне появляется поверх списка, не сдвигая содержимое, и само скрывается через несколько секунд.
- Переводы хранятся в файлах «ключ — значение», по одному на язык в `SonarHotkeys.Core/Strings`. Новый язык — это один файл, он сам появляется в меню языков.
- Язык выбирается в меню, которое всегда открывается под кнопкой.
- Щелчок по значку в трее открывает окно; меню в трее следует тёмной теме.
- Релиз — папка в ZIP-архиве, скомпилированная через Native AOT: 25 МБ вместо 44 МБ. Распакуйте папку `SonarHotkeys` и запустите `SonarHotkeys.exe`; устанавливать ничего не нужно.
- Нужна Windows 10 версии 1809 или новее.
- Настройки версии 1.x читаются как есть, включая сочетания вроде `Ctrl + Alt + D1`.
- В релиз добавлены условия лицензий распространяемых файлов Windows App SDK и WebView2 в папке `licenses/`.

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
