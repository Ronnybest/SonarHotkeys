# Changelog / Журнал изменений

## 3.0.0 — 2026-10-01

Presets for every Sonar channel. / Пресеты для всех каналов Sonar.

### English

- Every Sonar channel has its own presets: **Game, Chat, Media, Aux** and **Mic**. Each preset can switch a device too: an output device for the output channels and a microphone for Mic.
- Presets you mark as favorites in GG appear in their channel by themselves on start and on refresh. Your other presets are added with **Add**; a preset you remove is not added again.
- New cycling shortcuts, assigned by default: <kbd>Ctrl</kbd>+<kbd>Alt</kbd>+<kbd>F11</kbd> changes the cycled channel, <kbd>Ctrl</kbd>+<kbd>Alt</kbd>+<kbd>F12</kbd> steps through its presets, and <kbd>Ctrl</kbd>+<kbd>Alt</kbd>+<kbd>1</kbd>…<kbd>9</kbd> apply its preset with that number. The cycled channel is marked ⇄; changing it does not affect the sound.
- A preset's own shortcut works at any time, whichever channel is cycled, and leaves the cycled channel as it is.
- Drag a preset by its handle to change its place; the number shortcuts follow the new order.
- The preset Sonar has selected now is outlined, including one selected directly in GG. The window reads it from Sonar every two seconds while it is open, and the tray menu marks it too.
- **Switch all sound** setting, on by default: a preset's device routes every output channel, as before, or only its own channel when turned off. Streamer mode always switches the Personal mix.
- New window layout: channels on the left, the presets of the chosen channel on the right, and separate **Settings** and **Help** pages.
- Notifications appear at once instead of waiting for the previous one to close, so stepping through presets quickly shows each one.
- Settings of 2.x are converted: bindings become Game presets with the same devices. Their <kbd>Ctrl</kbd>+<kbd>Alt</kbd>+digit shortcuts give way to the number shortcuts of the same place. After the conversion, version 2.x cannot read the settings any more.

### Русский

- У каждого канала Sonar свои пресеты: **Game, Chat, Media, Aux** и **Mic**. Пресет может переключать и устройство: вывод у каналов вывода и микрофон у Mic.
- Пресеты, отмеченные избранными в GG, сами появляются в своих каналах при запуске и обновлении. Остальные ваши пресеты добавляются кнопкой **Добавить**; удалённый пресет больше не добавляется сам.
- Новые сочетания перебора, назначенные по умолчанию: <kbd>Ctrl</kbd>+<kbd>Alt</kbd>+<kbd>F11</kbd> меняет канал перебора, <kbd>Ctrl</kbd>+<kbd>Alt</kbd>+<kbd>F12</kbd> листает его пресеты, а <kbd>Ctrl</kbd>+<kbd>Alt</kbd>+<kbd>1</kbd>…<kbd>9</kbd> включают его пресет с этим номером. Канал перебора отмечен ⇄; его смена не трогает звук.
- Своё сочетание пресета работает всегда, какой бы канал ни перебирался, и не меняет канал перебора.
- Пресет перетаскивается за ручку на другое место; сочетания по номерам следуют новому порядку.
- Пресет, выбранный в Sonar сейчас, обведён рамкой, даже если его выбрали прямо в GG. Пока окно открыто, программа читает выбор из Sonar раз в две секунды; меню в трее тоже его отмечает.
- Настройка **Переключать весь звук**, по умолчанию включена: устройство пресета получают все каналы вывода, как раньше, а если выключить — только его канал. В режиме Streamer всегда меняется микс Personal.
- Новая компоновка окна: каналы слева, пресеты выбранного канала справа, отдельные страницы **Настройки** и **Помощь**.
- Уведомления появляются сразу, а не после закрытия предыдущего, поэтому при быстром перелистывании видно каждый пресет.
- Настройки 2.x переносятся: привязки становятся пресетами Game с теми же устройствами. Их сочетания <kbd>Ctrl</kbd>+<kbd>Alt</kbd>+цифра уступают место сочетаниям по номерам. После переноса версия 2.x настройки уже не прочитает.

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
