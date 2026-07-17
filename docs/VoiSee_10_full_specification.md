# VoiSee 10.x — полное техническое задание новой ветки

**Базовая стабильная версия:** VoiSee 9.2.7 Release Candidate / принятый релиз 9.2.7  
**Целевая ветка:** VoiSee 10.x  
**Назначение документа:** единый переносимый контекст для продолжения разработки в новом чате.

---

## 1. Цели ветки VoiSee 10

Ветка VoiSee 10 должна решить пять крупных задач:

1. Устранить удвоение звука при демонстрации экрана в Discord и других приложениях.
2. Заменить собственную CSS-подобную систему тем на прямую загрузку XAML ResourceDictionary.
3. Расширить управление файлами и категориями SoundBoard.
4. Завершить системную интеграцию: tray, single-instance и автозапуск.
5. Перестроить Settings, сохранив существующие рабочие функции, но убрав редко используемые элементы в Advanced Settings.

Ветка не должна ломать рабочие функции релиза 9.2.7: аудиомаршруты, SoundBoard, Voice Changer, Scenes, глобальные hotkeys, NumPad, Sound Editor, темы, установщик и сохранение настроек.

---

# 2. Предлагаемый порядок версий

## VoiSee 10.0 — Audio Routing Reliability

Первая версия ветки должна быть посвящена исправлению удвоения звука и стабилизации аудиомаршрутов. Это приоритетный блокер перед архитектурными и UI-изменениями.

## VoiSee 10.1 — Native XAML Themes

Замена CSS-движка тем на прямую работу с XAML ResourceDictionary.

## VoiSee 10.2 — SoundBoard File and Category Management

Контекстные операции с файлами, перенос и копирование между категориями, drag-and-drop к категориям и большой import overlay.

## VoiSee 10.3 — Tray, Single Instance and Autostart

Сворачивание в tray, восстановление существующего экземпляра и запуск вместе с Windows.

## VoiSee 10.4 — Settings Redesign

Новая трёхколоночная структура Settings и широкая панель Advanced Settings.

## VoiSee 10.5 — Integration, Installer and Release Hardening

Общий регрессионный прогон, миграции пользовательских данных, установщик, smoke-тесты и release candidate.

Разбиение может быть скорректировано в процессе разработки, но порядок зависимостей желательно сохранить.

---

# 3. VoiSee 10.0 — исправление удвоения звука при демонстрации экрана

## 3.1. Наблюдаемая ошибка

Если пользователь начинает демонстрацию экрана, например в Discord, звук VoiSee начинает восприниматься как сдвоенный/повторяющийся. Демонстрация может быть запущена не на окно VoiSee. Ошибка наблюдается даже тогда, когда Voice Monitor считается выключенным.

Необходимо отличать независимые маршруты:

- обработанный голос → Virtual Mic;
- обработанный голос → Headphones через Voice Monitor;
- SoundBoard → Virtual Mic;
- SoundBoard → Headphones;
- локальные cue/preview-сигналы;
- звук, который захватывает приложение демонстрации экрана.

## 3.2. Обязательная диагностика

Перед исправлением добавить временное диагностическое логирование маршрутов:

- создание и уничтожение WASAPI render/capture clients;
- устройство назначения каждого маршрута;
- состояние Voice Monitor;
- фактический коэффициент monitor route;
- SoundBoard → Headphones volume;
- Virtual Mic route volume;
- источник каждого `PlaySound`;
- запуск/остановка preview и локальных cue;
- количество активных экземпляров одного и того же playback key;
- повторное открытие аудиодвижка после изменения Windows/Discord audio session.

Логи не должны содержать персональные данные или содержимое звуков.

## 3.3. Матрица воспроизведения

Проверить минимум следующие сценарии:

1. Discord без демонстрации экрана, Voice Monitor Off.
2. Discord без демонстрации экрана, Voice Monitor On.
3. Демонстрация отдельного приложения без передачи звука.
4. Демонстрация отдельного приложения с передачей звука.
5. Демонстрация всего экрана с передачей системного звука.
6. Voice Monitor Off, SoundBoard → Headphones = 0%.
7. Voice Monitor Off, SoundBoard → Headphones > 0%.
8. Voice Monitor On, SoundBoard → Headphones > 0%.
9. Запуск обычного звука SoundBoard.
10. Запуск looped sound сцены.
11. Global Mute On/Off.
12. Sound Editor preview.
13. Повторный запуск VoiSee при уже работающем экземпляре после реализации single-instance.

## 3.4. Требуемое поведение

- При Voice Monitor Off обработанный голос не должен физически отправляться в устройство Headphones/Monitor.
- Выключение мониторинга должно останавливать или отключать monitor-provider, а не только визуально менять состояние кнопки.
- Один звук не должен одновременно запускаться двумя независимыми playback instance без явной причины.
- Preview и mute cue не должны попадать в Virtual Mic.
- Демонстрация экрана не должна вызывать повторное создание аудиомаршрута или второго экземпляра движка.
- Изменение default audio device или Discord session не должно дублировать подписки, callbacks или sample providers.
- После завершения screen share маршрутизация должна оставаться такой же, как до её запуска.

## 3.5. Stream-safe fallback

Если диагностика подтвердит, что второй сигнал создаёт не VoiSee, а одновременный захват Discord двух корректных путей — Virtual Mic и локального Headphones output — добавить **Stream-safe mode**.

Поведение Stream-safe mode:

- временно принудительно отключает все VoiSee → Headphones маршруты;
- не изменяет сохранённые положения пользовательских слайдеров;
- сохраняет Virtual Mic output;
- после выключения восстанавливает прежние уровни;
- состояние отчётливо видно пользователю;
- режим доступен из Settings/Advanced Settings и, при необходимости, из tray menu;
- автоматическое определение screen share не является обязательным, так как надёжно определить его для всех приложений невозможно.

Stream-safe mode добавляется только если исправление внутреннего дубля не устраняет проблему полностью.

## 3.6. Критерии приёмки VoiSee 10.0

- В Discord звук не двоится во всех согласованных тестовых сценариях.
- Voice Monitor Off подтверждается фактическим отсутствием monitor output.
- SoundBoard и сцены продолжают корректно идти в Virtual Mic.
- SoundBoard → Headphones остаётся независимой настройкой.
- Нет второго экземпляра одного playback key.
- Нет регрессии задержки, зависаний UI и global mute.
- Добавлен отдельный smoke-check аудиомаршрутов.

---

# 4. VoiSee 10.1 — переход с CSS на XAML-темы

## 4.1. Цель

Удалить собственный CSS-парсер и промежуточный слой преобразования стилей. Темы должны храниться и загружаться как XAML ResourceDictionary, который использует штатные механизмы WinUI 3.

Функционал панели Themes внешне не меняется.

## 4.2. Формат файлов

Новый формат:

```text
*.voiseetheme.xaml
```

Базовая структура:

```xml
<ResourceDictionary
    xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation"
    xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml">

    <!-- Brushes, Thickness, CornerRadius, Styles -->

</ResourceDictionary>
```

## 4.3. Семантические ресурсы

Не привязывать пользовательские темы к случайной структуре visual tree. Определить стабильный контракт ключей, например:

```text
VoiSee.AppBackgroundBrush
VoiSee.PanelBackgroundBrush
VoiSee.PanelBorderBrush
VoiSee.PrimaryTextBrush
VoiSee.SecondaryTextBrush
VoiSee.AccentBrush
VoiSee.DangerBrush
VoiSee.SuccessBrush
VoiSee.SoundButtonBackgroundBrush
VoiSee.SoundButtonForegroundBrush
VoiSee.TransportButtonStyle
VoiSee.SoundButtonStyle
VoiSee.SceneButtonStyle
VoiSee.DialogStyle
VoiSee.SliderStyle
VoiSee.ComboBoxStyle
VoiSee.ContextMenuStyle
VoiSee.CornerRadius.Small
VoiSee.CornerRadius.Medium
VoiSee.CornerRadius.Large
```

Полный список ключей должен быть документирован в sample theme.

## 4.4. Применение темы

- XAML-файл загружается как ResourceDictionary.
- Загруженный словарь добавляется или атомарно заменяется в `Application.Resources.MergedDictionaries`.
- Перед заменой выполняется проверка структуры и обязательных ключей.
- При ошибке текущая рабочая тема остаётся активной.
- Приложение не должно кратковременно показывать стандартную тему при переключении вкладок.
- Не должно быть повторного полного обхода всего visual tree на каждом tab switch.
- Live reload сохраняется.
- FileSystemWatcher должен иметь debounce и не реагировать несколько раз на одно сохранение файла.

## 4.5. Панель Themes

Сохраняются:

- список тем;
- выбор темы;
- Open Theme Folder;
- переименование пользовательской темы;
- удаление пользовательской темы;
- защищённая `Default Dark`;
- live reload.

Текст меняется:

- убрать упоминания CSS;
- объяснить формат `.voiseetheme.xaml`;
- указать, что тема является WinUI ResourceDictionary;
- дать краткое описание semantic resource keys;
- предупредить о fallback при ошибке XAML.

## 4.6. Миграция старых CSS-тем

Принятое базовое решение:

- runtime-поддержка CSS удаляется;
- старые файлы перемещаются в папку `Themes/Legacy CSS`;
- активная CSS-тема заменяется на `Default Dark`;
- пользователь получает одно уведомление о миграции;
- автоматический универсальный CSS→XAML-конвертер в приложение не включается;
- при необходимости позже можно сделать отдельный одноразовый внешний migration tool.

## 4.7. Критерии приёмки VoiSee 10.1

- CSS parser и CSS runtime полностью удалены.
- Default Dark визуально не хуже текущей темы 9.2.7.
- Все основные вкладки и диалоги используют XAML theme resources.
- Live reload работает без зависания и мерцания.
- Ошибочный XAML не приводит к падению.
- Переименование и удаление темы работают.
- Переключение вкладок остаётся быстрым.

---

# 5. VoiSee 10.2 — функции SoundBoard

## 5.1. Контекстное меню звука

Добавить новые команды:

1. **Show in File Explorer**.
2. **Move to Category**.
3. **Copy to Category**.

Существующие команды Edit, Rename, Hotkey, Delete и другие сохраняются.

## 5.2. Show in File Explorer

При выборе команды:

- открыть Windows Explorer;
- перейти в папку фактического файла;
- выделить конкретный файл через `/select,`;
- корректно обрабатывать пробелы, Unicode и длинные пути;
- если файл отсутствует — показать понятное сообщение и записать событие в лог.

## 5.3. Move to Category

- показать список существующих категорий;
- текущую категорию отметить или отключить;
- после выбора изменить `CategoryId` существующего объекта;
- сохранить ID звука;
- сохранить hotkey;
- сохранить usage count и timestamps;
- сохранить ссылки сцен на этот звук;
- физический аудиофайл не перемещать;
- обновить список SoundBoard без полного перезапуска движка.

## 5.4. Copy to Category

- создать новый объект `SoundBoardSound` с новым ID;
- физически скопировать аудиофайл в управляемую папку VoiSee;
- назначить выбранную категорию;
- hotkey не копировать, чтобы не создать конфликт;
- usage count сбросить;
- сцены не должны автоматически ссылаться на копию;
- имя копии формировать как:

```text
Название [copy]
Название [copy 2]
Название [copy 3]
```

- имя физического файла также должно быть уникальным;
- редактирование копии через Sound Editor не должно менять оригинал.

## 5.5. Drag-and-drop звука к ComboBox категорий

Пользователь должен иметь возможность перетянуть существующий звук SoundBoard к выпадающему списку категорий.

Поведение:

- при наведении на ComboBox запускается задержка раскрытия примерно 400–700 мс;
- ComboBox автоматически открывается;
- категория под курсором подсвечивается;
- Drop на категорию выполняет Move;
- `Ctrl + Drop` выполняет Copy;
- Escape отменяет операцию;
- отпускание вне списка ничего не меняет;
- показывается визуальная подсказка `Move` или `Copy`;
- нельзя случайно создать дубликат из-за нескольких Drop events;
- после операции список и выбор категории обновляются.

## 5.6. Большой import overlay

При перетаскивании файлов из Проводника overlay должен быть явно заметен.

Требования:

- находится по центру клиентской области главного окна;
- занимает примерно 75% ширины и 75% высоты окна;
- прямоугольник немного меньше основной области приложения;
- отображается поверх любой основной вкладки;
- крупная иконка импорта;
- крупный текст `Drop audio files to import`;
- поддержка нескольких файлов;
- дополнительная строка с целевой категорией;
- при наведении на неподдерживаемые файлы показывать причину отказа;
- исчезает после Drop, DragLeave, Escape и потери drag session;
- overlay не должен оставаться зависшим;
- overlay не должен ломать прокрутку SoundBoard Gate 6.8.

## 5.7. Критерии приёмки VoiSee 10.2

- Show in Explorer открывает и выделяет правильный файл.
- Move сохраняет идентичность звука и сцены.
- Copy создаёт независимый файл и объект.
- Drag-to-category работает как Move и Ctrl+Copy.
- Overlay заметен на экранах разных размеров и DPI.
- Импорт нескольких файлов работает.
- Пользовательские данные не попадают в установщик.

---

# 6. VoiSee 10.3 — tray, single-instance и autostart

## 6.1. Закрытие в tray

Нажатие крестика главного окна по умолчанию:

- не завершает процесс;
- скрывает главное окно;
- оставляет аудиодвижок работающим;
- оставляет global hotkeys активными;
- оставляет сцены и looped sounds в текущем состоянии;
- показывает иконку VoiSee в notification area.

Иконка:

- фирменный логотип VoiSee;
- прозрачный фон;
- корректное отображение в светлой и тёмной панели Windows;
- отдельный ресурс подходящего размера.

## 6.2. Tray menu

Минимум:

```text
Open VoiSee
────────────
Exit VoiSee
```

Допустимые будущие элементы, но не обязательные для первой реализации:

- Virtual Mic Mute;
- Stream-safe mode;
- текущая сцена;
- Audio Engine status.

## 6.3. Восстановление окна

Окно восстанавливается:

- двойным кликом по tray icon;
- командой Open VoiSee;
- повторным запуском VoiSee.exe;
- запуском через ярлык при уже работающем экземпляре.

При восстановлении:

- окно становится видимым;
- возвращается в normal state, если было minimized;
- выводится на передний план;
- активируется;
- сохраняет предыдущую вкладку и UI state.

## 6.4. Single-instance

Разрешён только один экземпляр VoiSee на пользовательскую сессию.

Второй запуск должен:

- обнаружить существующий экземпляр;
- отправить ему сигнал Activate/Show;
- не создавать второе главное окно;
- не запускать второй аудиодвижок;
- не регистрировать hotkeys повторно;
- не открывать второй доступ к JSON/библиотеке;
- завершиться после успешной передачи сигнала.

Предпочтительная реализация:

- AppInstance/redirect activation, если стабильно работает с текущим WinUI unpackaged setup;
- иначе named mutex + named pipe/локальный IPC;
- обработка должна работать и после запуска из tray/autostart.

## 6.5. Настоящий выход

Команда Exit VoiSee должна:

- остановить preview и SoundBoard playback;
- остановить looped sounds;
- корректно остановить аудиодвижок;
- снять keyboard/mouse hooks;
- освободить global hotkeys;
- остановить FileSystemWatcher тем;
- удалить tray icon;
- сохранить настройки;
- завершить процесс без зависания.

## 6.6. Autostart

В Settings добавить checkbox:

```text
Start VoiSee with Windows
```

Поведение:

- регистрация только для текущего пользователя;
- не требует прав администратора;
- путь корректно заключён в кавычки;
- не создаёт дубликаты;
- отключение удаляет регистрацию;
- checkbox показывает фактическое состояние Windows, а не только JSON;
- если путь после обновления изменился, регистрация исправляется;
- uninstall удаляет autostart entry.

Режим запуска:

```text
VoiSee.exe --background
```

При `--background`:

- приложение запускается скрытым;
- tray icon создаётся;
- аудиодвижок и hotkeys инициализируются;
- главное окно не показывается до запроса пользователя.

При обычном ручном запуске главное окно показывается.

## 6.7. Критерии приёмки VoiSee 10.3

- Крестик скрывает окно, но звук и hotkeys продолжают работать.
- Exit полностью завершает процесс.
- Повторный запуск разворачивает существующее окно.
- В Task Manager нет второго экземпляра с активным движком.
- Autostart запускает приложение скрытым в tray.
- После обновления autostart ведёт на актуальный путь.
- Tray icon не имеет непрозрачного фона.

---

# 7. VoiSee 10.4 — новый Settings

Settings остаётся трёхколоночным.

## 7.1. Первый столбец — System & Audio

### 7.1.1. VB-CABLE status

Основная карточка состояния:

```text
VB-CABLE is working normally
```

При нормальной работе:

- спокойный положительный статус;
- не показывать лишние инструкции.

При проблеме:

- объяснить, что именно не найдено;
- показать существующую кнопку установки/открытия инструкции;
- показать необходимость ручного подтверждения драйвера, если применимо;
- после установки предложить Refresh Devices.

### 7.1.2. Audio Devices

Включить:

- Input Device;
- Virtual Output;
- Monitor / Headphones;
- Refresh Devices;
- SoundBoard Delay;
- основные регуляторы маршрутов, которые пользователь меняет регулярно.

SoundBoard Delay переносится сюда из отдельной/старой области.

### 7.1.3. Hotkeys

Сохранить существующее управление hotkeys.

Обязательные свойства:

- NumPad;
- захват комбинации кнопкой;
- отображение конфликтов;
- приоритет:

```text
Transport → Scene → SoundBoard → Voice Preset
```

- hotkeys Sound Editor продолжают иметь специальную логику;
- при открытом Sound Editor обычные внешние звуки блокируются, как в 9.2.7.

### 7.1.4. Autostart

Checkbox:

```text
Start VoiSee with Windows
```

Дополнительная подсказка:

```text
VoiSee starts in the notification area and keeps audio hotkeys available.
```

### 7.1.5. Advanced Settings

В основном столбце остаётся одна кнопка открытия расширенной панели.

Панель должна быть шире текущего logs dialog, чтобы в ней помещались две части.

Слева — **Engine Manual Control**:

- текущий статус движка;
- Start Engine;
- Stop Engine;
- Restart Engine;
- текущее Input Device;
- текущее Virtual Output;
- текущее Monitor Device;
- краткая информация о состоянии маршрутов;
- при наличии — Stream-safe mode.

Справа — **Logs**:

- существующий лог viewer;
- Clear;
- Copy;
- Export, если уже реализовано;
- автопрокрутка;
- понятное отображение ошибок маршрутов.

Logs и manual engine controls убрать из основной страницы Settings.

## 7.2. Второй столбец — Themes

Внешний вид и функции панели сохраняются.

Изменить текст:

- убрать CSS;
- описать `.voiseetheme.xaml`;
- указать ResourceDictionary;
- показать путь к папке Themes;
- объяснить live reload;
- дать ссылку/кнопку на sample theme или открыть sample file;
- описать fallback при ошибке XAML.

## 7.3. Третий столбец — About Me

Сохранить текущую структуру и ссылки, кроме Telegram.

Новый Telegram:

```text
https://t.me/VoriXdev
```

Ссылка должна открываться системным браузером.

## 7.4. Адаптивность Settings

- Три колонки отображаются при достаточной ширине.
- На небольшом окне появляется корректная прокрутка, а не обрезание.
- Колесо мыши прокручивает Settings, а не соседнюю вкладку.
- Advanced Settings является модальным или отдельным centered dialog и имеет собственную прокрутку.
- Theme live reload не сбрасывает положение прокрутки.

## 7.5. Критерии приёмки VoiSee 10.4

- Основные настройки видны без перегрузки.
- Logs и Engine controls находятся только в Advanced Settings.
- SoundBoard Delay находится в Audio Devices.
- Autostart checkbox отражает реальное состояние.
- Themes содержит только XAML-терминологию.
- Telegram ведёт на `https://t.me/VoriXdev`.
- Настройки не выходят за границы окна.

---

# 8. VoiSee 10.5 — интеграция и релиз

## 8.1. Миграции

Обязательные миграции при первом запуске VoiSee 10:

- сохранить существующую библиотеку SoundBoard;
- сохранить категории;
- сохранить сцены;
- сохранить voice presets;
- сохранить hotkeys;
- сохранить аудиоустройства;
- перенести CSS-темы в `Legacy CSS`;
- выбрать Default Dark, если активна старая CSS-тема;
- не сбрасывать пользовательские уровни громкости;
- не менять файлы звуков без операции пользователя.

Миграция должна быть идемпотентной: повторный запуск не повторяет перенос.

## 8.2. Установщик

Проверить:

- версия VoiSee 10.x во всех metadata;
- правильное имя папки `VoiSee`;
- отсутствие консольного окна;
- self-contained Windows App SDK;
- отсутствие пользовательских данных в установщике;
- transparent tray icon включён в ресурсы;
- uninstall удаляет autostart registration;
- uninstall корректно предлагает закрыть tray process;
- обновление поверх 9.2.7 сохраняет данные;
- VB-CABLE flow не регрессировал.

## 8.3. Обязательный регрессионный набор

### Audio

- microphone → Virtual Mic;
- Voice Monitor On/Off;
- SoundBoard → Virtual Mic;
- SoundBoard → Headphones;
- Virtual Mic Master / Global Mute;
- screen-share duplication matrix;
- preview cue не идёт в Virtual Mic;
- переключение устройств;
- restart engine;
- sleep/resume Windows;
- reconnect USB microphone/headphones.

### SoundBoard

- импорт;
- большие drag overlays;
- категории;
- Move;
- Copy;
- Show in Explorer;
- hotkeys;
- NumPad;
- loop;
- timeline;
- поиск;
- прокрутка Gate 6.8.

### Sound Editor

- drag selection;
- Trim Outside;
- Cut Selection;
- playhead;
- preview from start;
- preview from selection;
- SoundBoard headphones volume;
- effects;
- live waveform;
- Save File;
- Save as `[edit]`;
- scroll routing;
- external hotkey blocking;
- temp file cleanup.

### Voice Changer

- presets;
- icon picker;
- scroll zones;
- DSP sliders;
- restore settings;
- push-to-talk.

### Scenes

- activate/deactivate;
- looped sound;
- buttons;
- independent volumes;
- action hotkeys;
- scene persistence.

### Themes

- Default Dark;
- XAML custom theme;
- live reload;
- invalid XAML fallback;
- rename;
- delete;
- open folder;
- no tab-switch flash.

### System integration

- close to tray;
- open from tray;
- Exit;
- second launch activates first;
- autostart background;
- no duplicate engine/hotkeys;
- update installer;
- uninstall.

## 8.4. Release gates

Релиз VoiSee 10 считается готовым только после:

1. `dotnet clean` и Release build без ошибок и предупреждений, требующих исправления.
2. Успешного ручного запуска unpackaged приложения.
3. Успешной сборки установщика.
4. Установки поверх чистой системы/профиля.
5. Обновления поверх VoiSee 9.2.7.
6. Прохождения автоматических smoke-тестов.
7. Прохождения ручной screen-share matrix.
8. Проверки tray и single-instance после нескольких циклов.
9. Проверки пользовательских данных после upgrade/uninstall/reinstall.
10. Создания release notes и SHA-256 финального ZIP/installer.

---

# 9. Архитектурные ограничения

- Не возвращать тяжёлые синхронные операции в UI thread.
- Не ломать рабочую прокрутку SoundBoard Gate 6.8.
- Не применять тему полным обходом visual tree при каждом tab switch.
- Не хранить пользовательские данные рядом с publish output.
- Не включать пользовательские звуки/сцены/темы в установщик.
- Не создавать второй аудиодвижок при повторном запуске.
- Не использовать process-kill как штатный способ Exit.
- Не изменять исходный аудиофайл до явного Save File.
- Не смешивать preview/cue с Virtual Mic.
- Не считать выключенный UI toggle достаточным доказательством выключенного аудиомаршрута — проверять фактический provider/client state.

---

# 10. Что не входит в текущую ветку

- визуальный WYSIWYG-редактор XAML-тем;
- универсальный CSS→XAML-конвертер внутри VoiSee;
- облачная синхронизация;
- marketplace тем;
- управление всей библиотекой из tray;
- автоматическое распознавание Discord screen sharing как обязательная функция;
- массовое редактирование десятков звуков одновременно;
- собственный виртуальный аудиодрайвер вместо VB-CABLE;
- интеграция Яндекс Музыки;
- расширение набора эффектов Sound Editor, кроме отдельных согласованных обновлений.

---

# 11. Принятые решения и допущения

1. Вся новая ветка называется **VoiSee 10.x**, а не 9.3–9.6.
2. Исправление screen-share audio duplication выполняется первым.
3. Старые CSS-темы архивируются, но не поддерживаются в runtime.
4. Autostart запускает VoiSee скрытым в tray через `--background`.
5. Закрытие крестиком скрывает приложение, а настоящее завершение выполняется только через `Exit VoiSee`.
6. Copy to Category создаёт физически независимую копию файла.
7. Move to Category не двигает физический файл и не меняет ID звука.
8. Обычный drag в категорию означает Move, Ctrl+drag означает Copy.
9. Telegram в About Me: `https://t.me/VoriXdev`.
10. Если удвоение при screen share вызвано внешним двойным захватом корректных маршрутов, допускается ручной Stream-safe mode без ненадёжного автоматического определения Discord.

---

# 12. Рекомендуемая первая задача реализации

Начать с отдельной ветки:

```text
feature/voisee-10-audio-routing-diagnostics
```

Первый шаг:

1. Зафиксировать архив/commit VoiSee 9.2.7 как baseline.
2. Добавить route-level logging без изменения поведения.
3. Воспроизвести проблему по матрице Discord screen share.
4. Определить источник второго сигнала.
5. Исправить маршрутизацию или добавить Stream-safe fallback.
6. Выпустить VoiSee 10.0 audio-routing build для ручной проверки.

Только после подтверждения исправления переходить к удалению CSS theme engine.

---

**Конец технического задания.**
