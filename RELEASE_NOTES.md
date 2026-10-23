# RoAura 2.9.0

## 2.9.0

- All gameplay overlays now use coordinates relative to the Ragnarok client area instead of desktop coordinates.
- The overlay host follows the game window when it is moved or resized and remains clipped to its client area.
- Existing screen-based coordinates are converted automatically once, including saved presets.
- Cooldowns, effect notifications, reminders, buff panels, equipment, MVP timers, rewards and rate indicators are constrained to the game area.
- Overlays no longer remain above unrelated applications when Ragnarok is not active.
- Database and market panels now use the Ragnarok client area rather than the outer window frame.
- Removed the CS1998 build warning from the database mode handler.

## 2.8.5

- Fixed a startup crash caused by the database section change handler accessing the Search button before WPF finished initializing the window.

## 2.8.4

- Market searches no longer start automatically with an empty query.
- The previous database result and details are cleared immediately while a market search is running.
- Market responses are cached for two minutes, repeated searches are instant, and stale empty results expire quickly.
- A normal successful search loads one SpringRO page instead of two; the second page is checked only when needed.
- Starting a new search, changing sections, or closing the overlay cancels the obsolete request.

## 2.8.3

- Поиск в SpringRO Market теперь фильтрует результаты строго по названию предмета; совпадения только по названию лавки или владельцу исключаются.
- В режиме маркета добавлена подсказка «Поиск только по названию предмета».

## 2.8.2

- Тайм-аут SpringRO Market увеличен с 15 до 60 секунд: медленный ответ сайта больше не обрывается раньше времени.
- Добавлены браузерные HTTP-заголовки, поддержка gzip/deflate/brotli и одна автоматическая повторная попытка.

## 2.8.1

- Исправлена сборка сервиса SpringRO Market: явно подключены `System.Net.Http`, потоки и задачи.

## 2.8.0

- Добавлен живой раздел SpringRO Market с режимами «Продажа» и «Покупка» и поиском по предмету, магазину или владельцу.
- Карточка предложения показывает цену, количество, заточку, карты, магазин, владельца и координаты.
- Кнопка Navi копирует команду `/navi map x/y` в буфер обмена.
- Окна базы больше не скрываются физически при потере фокуса: вне Ragnarok они снимаются с режима Topmost, а при возврате в игру снова поднимаются поверх клиента.

## 2.7.5

- Максимизированное окно теперь ограничивается рабочей областью текущего монитора: нижние кнопки не обрезаются панелью задач или краем экрана.
- Для карт SpringRO рассчитана плотность опыта: средний Base EXP и Job EXP за одного монстра с учётом численности каждого вида на карте.
- Плотность опыта показывается рядом с названием карты в результатах и отдельным блоком в карточке карты.

## 2.7.4

- Окно базы больше не исчезает при первом клике: добавлена защита от краткого промежуточного состояния фокуса.
- Блокировка клавиатуры действует только когда активен процесс Ragnarok Online; другие приложения больше не блокируются.
- Исправлены поле поиска и обе кнопки закрытия, включая обрезанную кнопку правой панели.
- Убрана прямоугольная подложка за скруглёнными углами окон базы.

## 2.7.3

- Opening the database no longer hides the main RoAura settings window.
- F8 remains available while database keyboard blocking is active.
- Database panels remain open when the player clicks inside the RO client.
- Panels automatically hide over unrelated applications and return when RO becomes active.
- Added repeated native topmost positioning for borderless/fullscreen-optimized clients.
- Fixed crowded database category labels and added wrapping for long detail links.

## 2.7.2

- Replaced the large centered database window with two compact edge panels.
- Search and the result list are anchored to the left edge of the active RO client.
- The selected monster/item/map card is anchored to the right edge.
- The center of the game is now completely unobstructed and receives mouse clicks directly.
- Reduced search, result row, header and detail-card dimensions.

## 2.7.1

- Fixed initialization of the database service in the in-game database overlay.
- Removed the CS8618 and CS0649 build warnings for `DatabaseOverlayWindow._database`.

## 2.7.0

- Added the in-game SpringRO database overlay with a configurable F7 hotkey.
- Added fast offline search across 281 monsters, 1,370 items and 171 maps.
- Added linked navigation: monster drops and maps, item drop sources, and map monster lists.
- The database overlay is positioned and sized inside the Ragnarok Online client window.
- Keyboard focus stays in the database while it is open; pressing the hotkey again closes it and restores the game.

## 2.6.2

- Rebuilt preset management around immutable snapshots and a separate mutable working copy.
- Split preset creation from selection to prevent accidental overwrites.
- Added explicit Load, Update selected, Create new and Delete actions.
- Loading a preset now preserves the previous working configuration in Recovery.
- All settings and preset writes are atomic.

## 2.6.1

- Fixed preset isolation: autosave now writes only to the mutable working settings and cannot overwrite a saved preset.
- Applying a preset copies its snapshot into the working settings before restart.
- Explicitly overwriting a preset now creates a timestamped backup; the ten newest backups are retained.

## 2.6.0

- Added independent RTM and SpringRO item databases for loot and equipment names.
- Removed the global Item ID 12333 → Black Heart override; names now come only from the selected server database.
- Added independent RTM and SpringRO MVP catalogs. SpringRO contains only the 19 MVPs marked in the supplied database.
- Added named presets that save the complete application configuration and selected server databases.
- Switching presets restarts RoAura automatically with the selected configuration.

- Fixed the primary cause of intermittent packet tracking loss: saving settings, moving overlays, hiding the window and completing ID learning no longer restart Npcap or clear the TCP/equipment session when the capture endpoint has not changed.
- Npcap is now restarted only after an actual enabled/IP/port change or an explicit Npcap check. A real capture-session change resets stale AID and packet-derived overlay state safely.
- A missed TCP segment no longer blocks the stream waiting forever. RoAura drops only the incomplete packet tail and immediately resynchronizes on following data.
- Reused TCP connections with restarted sequence numbers are detected and recovered instead of being mistaken for endless retransmissions.
- Npcap adapters that stop after a VPN, ExitLag or route transition are reopened automatically without requiring an AID reset or character relog.
- Equipment identity caches now survive every ordinary UI save, so a successful learning event is not erased immediately and later swaps remain observable while RO is inactive.

## 2.5.3

- Fixed tracked equipment remaining in `not equipped` after a valid `0x0999` event. Matching now uses strict priority: exact variant fingerprint, then current-session inventory index plus Item ID, then Item ID only for legacy entries without a fingerprint.
- The previous item in a wear slot is no longer cleared before RoAura has fully resolved the incoming equipment event; a tracked target is then selected using the prioritized matcher.
- Removed the two-second post-login suppression that could discard a legitimate equip action and make learning succeed only intermittently.
- Equipment state continues to be maintained by packet events independently of which application currently owns keyboard focus.
- Added TCP sequence reassembly and cross-adapter retransmission deduplication. Split packets are processed in order, while delayed duplicates from Ethernet, loopback, ExitLag or VPN adapters can no longer restore stale equipment state after Alt-Tab.
- Added detailed logging for fully resolved but unmatched equipment packets.

## 2.5.2

- Unified the equipment identity cache across every Npcap adapter. Inventory packets captured on Ethernet, loopback, ExitLag or VPN interfaces can now resolve equip acknowledgements captured on another adapter.
- Added a pending equipment-event queue: if `0x0999`/`0x099A` arrives before the inventory identity table, the event is replayed with the complete Item ID, refine, cards and options as soon as the identity becomes available.
- Suppressed unidentified equipment events instead of letting them incorrectly clear the currently displayed item in the same wear slot.
- Capture generations now reject late packets from an obsolete adapter session after automatic IP/port switching.

## 2.5.1

- Fixed equipment learning after a cache-miss: the failed attempt is now disarmed before a relog, so login synchronization can no longer replace the selected entry with another equipped item (commonly the weapon).
- Equipment fields are committed only after Item ID and the complete variant fingerprint have been resolved; incomplete or false packets no longer overwrite the current entry.
- Inventory snapshots remain available for restoring overlay state but no longer count as a deliberate learning action.
- Ignored the short burst of repeated equip acknowledgements that some clients send immediately after an inventory snapshot during login.
- Fixed stale 20-second learning timers so an older failed attempt cannot cancel a newer retry for the same entry.
- Added rejection and logging for implausible unresolved equipment indexes caused by packet-ID byte patterns inside unrelated TCP data.

## 2.5.0

- Added a configurable icon-to-background gap to Cooldowns, Buff/Debuff notifications, Buff Panel groups, Reminder and Equipment; `0 px` gives the tightest image fill.
- Added automatic IP/port detection from established TCP connections owned by configured Ragnarok processes, plus a manual `Detect now` action and periodic endpoint refresh.
- Replaced left navigation glyphs with the supplied seven 50×50 Ragnarok-style PNG icons.
- Fixed the RTM `0x0B39` inventory type: `0` is inventory and `1` is cart. The previous mapping discarded the equipment identity table, leaving only Index and Slot.
- Equipment identity is now also cached from `0x0B41` item-add packets, including Item ID, refine, grade, four cards and random options.
- Normalized random-option fingerprints across inventory-list and item-add packets.

## 2.4.0

- Fixed RTM equipment inventory parsing by supporting both 67-byte pre-grade and 68-byte grade-aware `0x0B39` item records.
- Increased TCP carry storage so large inventory packets can be reconstructed instead of losing their header between network segments.
- Equipment learning now stores a variant fingerprint containing Item ID, refine level, grade, four card IDs and random options.
- Items with the same base Item ID but different cards, refinement or options can now be tracked independently.
- Added a readable detected-item field using the bundled RTM item and card names.

## 2.3.3

- Added a compatibility rendering mode that keeps WPF and the transparent overlay off the GPU used by legacy Ragnarok Direct3D clients and dgVoodoo.
- Equipment, status and skill learning no longer restart every Npcap adapter when packet capture is already running.
- Reduced the overlay refresh rate from 20 to 10 updates per second to lower compositor load without affecting packet processing or displayed cooldown accuracy.

## 2.3.2

- Restored the custom title-bar maximize/restore button, including the matching window-state glyph.
- Fixed the constrained scrollbar in ComboBox drop-down lists; the track now uses the full available height and keeps a usable thumb size.
- Introduced a Ragnarok-inspired typography hierarchy: Palatino Linotype for display and section titles, Trebuchet MS for navigation, labels and button captions.

## 2.3.1

- Added the first `Ragnarok Remastered` visual pass: custom window chrome, buttons, fields, selectors, checkboxes, tables, cards and scrollbars.
- Rebuilt the Cooldowns page as the reference design for the remaining modules.
- Added distinct primary, gold, danger, ghost and placement-control states.
- Removed the duplicate About button from the bottom bar; About remains in the left navigation.

## 2.3.0

- Rebuilt the settings window in a light Ragnarok Online-inspired blue/parchment style.
- Added persistent left navigation for Cooldowns, Buffs / Debuffs, Reminder, Equipment, MVP Timers and Loot / Zeny / EXP.
- Removed the separate right-side preview cards and widened the practical settings area.
- Preserved every existing packet-capture, ID-learning, overlay-placement and notification control.

## 2.2.2

- Added `Reset session` for farming statistics. It clears accumulated EXP/Zeny and elapsed rate time without resetting AID, current EXP, the level requirement or packet capture.
- A fresh statistics session begins with the next EXP or Zeny event, preventing long idle periods from permanently distorting hourly rates and time-to-level estimates.

## 2.2.1

- Added a configurable 1–60 second refresh interval for farming statistics; the default is 5 seconds to reduce visual noise.
- Added numbered groups and group placement mode for XP/hour, time-to-level and Zeny/hour indicators.
- Moved `Short names` and `READY label` from global settings into the Cooldowns tab.

## 2.2.0

- Fixed RTM Item ID 12333 so loot notifications display `Black Heart` instead of a numeric fallback.
- Expanded the bundled RTM item catalog with item categories and separate configurable colors for Equip, Card, Loot and Consumables.
- Added independently configurable XP/hour, time-to-level and Zeny/hour overlay indicators with individual enable flags and mouse placement.
- Fixed the Loot / Zeny / EXP General Settings help text so it wraps inside the visible settings area instead of forcing horizontal scrolling.

## 2.1.1

- Fixed a startup crash caused by a two-way WPF binding to the read-only reward channel name.
- Added a visible fatal-error dialog and detailed `debug.log` entry for future UI startup failures.

## 2.1.0

- Added packet-based inventory notifications with RTM item names and aggregation by Item ID, so repeated pickups become one `Item xN` line.
- Added aggregated positive Zeny notifications for rapid multi-monster rewards.
- Added Base/Job EXP notifications with the gained amount and percentage of the current level requirement.
- EXP safely omits the percentage until the server has supplied the current level requirement.
- Loot, Zeny and EXP now have independent colors, enable flags, display order and numbered notification groups.
- Every reward group has independent position, text size, duration, aggregation window, vertical gap and maximum visible line count, plus mouse placement and a test preview.
- Bundled the 3,827-entry RTM item catalog in both portable and installer releases.

## 2.0.1

- Fixed RTM equipment learning: `0x0999`/`0x099A` acknowledgements now use RTM/rAthena's inverted result semantics, and the `0x0B39` inventory packet supplies the Item ID mapping.
- Replaced the generic monster catalog with the 95 MVP records from RTM `db-mobs.json`; search now happens directly inside the editable MVP field by name or ID.
- MVP timer overlay is hidden whenever there are no running timers.
- Added an optional per-timer completion sound, volume control, and sound test.

## 2.0.0

- Added the Equipment overlay with packet-based learning, Item ID/inventory index/wear-slot tracking, icon/text appearance settings, individual/group placement, and live equipped state.
- Added independent MVP Timers with an editable JSON monster catalog, name/ID search, manual duration, restart/reset controls, a top-to-bottom overlay list, movable panel, and configurable visibility hotkey.
- Buffs / Debuffs now use the server-provided remaining duration. Active-effect icons can show a numeric countdown, remaining-time fill, both, or neither.
- Settings migration keeps existing cooldowns, effects, reminders, panel groups, hotkeys, and positions intact.
- Added Russian and English UI strings and updated both user guides for the new modules.

## 1.2.3

- Fixed stretched editor fields in Cooldowns and Reminder: every settings row now uses automatic height and every editor has a stable 28 px height.
- Added explicit four-sided borders to all text fields.
- Renamed ready/cooldown and active/inactive values so each label clearly states that it controls opacity.
- Split Buff Panel position/size and opacity settings into separate rows for cleaner spacing.
- Reflowed Reminder appearance into a vertical form so long color labels are never clipped.
- Removed the duplicated in-window About heading and simplified the development year to `2026`.

## 1.2.2

- Rebuilt the main layout so tabs, action log and footer never overlap.
- Added dedicated, scrollable settings cards to all three tabs.
- Cooldowns now has detailed main parameters, appearance and icon preview panels without removing table columns.
- Reminder now uses the same structured layout with a centered preview.
- Buffs / Debuffs appearance, sound and Buff Panel settings have consistent spacing and no longer collide when the window is resized.
- Separated packet capture status from the last-action log and added safe text trimming.

## 1.2.1

- Replaced exclusive Windows hotkey registration with non-reserving key polling; startup F8/F9/ESC conflict warnings are gone.
- Added configurable hotkeys for settings, overlay visibility and finishing placement.
- Restored the Buff Panel as independent numbered groups. Each group lays effects left-to-right and has its own position, icon size, and active/inactive opacity.
- Buff/debuff notifications now stack downward independently inside each numbered group.
- Expanded Reminder appearance: optional icon, background and border, independent icon/text sizes, colors, opacity, and centered placement.
- Fixed clipped links in the About window and updated the displayed version.
- Preserved separate appeared/disappeared sounds and volume controls.
- Discord: http://discord.gg/aleriontv
# RoAura 2.9.1

- Исправлен `NullReferenceException` при запуске: XAML-событие выбора режима базы данных больше не обращается к ещё не созданным элементам окна.
- Настройки и база данных присваиваются до построения визуального дерева, что исключает повторение ошибки при ранних событиях WPF.
- Сохранена привязка всех игровых оверлеев к клиентской области окна Ragnarok Online.

# RoAura 2.11.0

- Добавлена автоматическая проверка последнего стабильного релиза через публичный GitHub Releases API.
- При наличии новой версии RoAura показывает отдельное окно с версией и описанием изменений.
- Добавлена кнопка «Проверить обновления» / `Check for updates` в окне «О программе».
- Установщик новой версии скачивается автоматически с отображением прогресса.
- Перед запуском обновления выполняется обязательная проверка SHA-256 по digest, опубликованному GitHub.
- Обновление устанавливается без удаления настроек и пресетов из `%APPDATA%\RoAura`.
- Ошибки сети и установки записываются в диагностический лог; доступен переход на страницу релиза для ручного обновления.
- В Setup добавлен отсутствовавший файл `Data\database.springro.json`, необходимый игровому оверлею базы SpringRO.

# RoAura 2.12.1

- Исправлена блокировка временного файла при автоматическом обновлении: поток загрузки теперь гарантированно закрывается до проверки SHA-256, переименования и запуска установщика.
- Расширенный поиск монстров получил читаемые подписи полей и корректное раскрытие без наложения текста.
- Карточка монстра дополнена полными Stats, Element Damage, Skills и Behavior из базы SpringRO.
- Маркет ищет мгновенно по локальному снимку и обновляет результаты автоматически после фоновой загрузки.
- Окна базы привязаны к окну Ragnarok как owned overlays, чтобы первый клик по игре не прятал их за полноэкранным клиентом; Alt, Ctrl и Shift пропускаются системным хуком.
- Дополнена английская локализация новых функций и оверлеев.

## English

- Fixed the temporary-file lock that prevented the automatic updater from verifying and launching the installer.
- Rebuilt Advanced Monster Search with visible field labels and a clean expandable layout.
- Monster details now include complete Stats, Element Damage, Skills, and Behavior from SpringRO.
- Market searches now return immediately from a local snapshot and refresh automatically in the background.
- Database panels are owned by the Ragnarok window so the first in-game click no longer sends them behind fullscreen clients, while Alt, Ctrl, and Shift remain available.
- Completed English localization for the new database controls and overlay messages.

# RoAura 2.13.0

## Русский

- Добавлен новый раздел Gold Goblin: таблица всей добычи за сессию с количеством, ценой за единицу и общей стоимостью.
- В комплект включена отдельная база NPC sell value, собранная со страниц предметов SpringRO.
- Добавлены особые правила цены: минимальная цена продажи с Vending и ручная цена с автоматическим fallback.
- Добавлена настройка Overcharge 0–10; бонус применяется только к предметам, оцениваемым по NPC.
- Loot-уведомления могут показывать стоимость предмета в Zeny, а Zeny per hour — учитывать стоимость добычи Gold Goblin.
- Добавлены отдельные пиксельные иконки Gold Goblin и базы данных.

## English

- Added the new Gold Goblin section: a session loot table with item count, unit cost, and total value.
- Bundled a dedicated NPC sell-value database collected from the official SpringRO item pages.
- Added special pricing rules using the lowest current Vending sell price and a manual fallback price.
- Added Overcharge levels 0–10; the bonus applies only to NPC-valued items.
- Loot notifications can show the calculated Zeny value, and Zeny per hour can include Gold Goblin loot value.
- Added dedicated pixel-art icons for Gold Goblin and the database.

# RoAura 2.12.2

- Исправлен запуск RoAura через галочку «Запустить RoAura» на последней странице установщика: приложение теперь запускается системным ShellExecute с корректной обработкой UAC вместо ошибки CreateProcess 740.
- Для запуска из установщика явно задаётся рабочая папка приложения.

## English

- Fixed the “Launch RoAura” checkbox on the final installer page. The application now starts through Windows ShellExecute, allowing UAC to be handled correctly instead of producing CreateProcess error 740.
- The installer now explicitly sets the RoAura installation directory as the application working directory.

# RoAura 2.12.0

- Карточки предметов SpringRO показывают изображение, описание, NPC sell value и актуальное количество экземпляров на сервере.
- Для монстров добавлен расширенный поиск по расе, элементу, размеру, уровню, HP, Base/Job EXP, уровню элемента, Aggressive, Boss и MVP.
- Маркет полностью обновляется в фоне и сохраняется локально, поэтому поиск использует готовый снимок вместо ожидания SpringRO.
- В настройках базы добавлен интервал фонового обновления маркета.
- Карточка торгового предложения показывает миникарту и точное положение торговца красной отметкой.
- Оверлеи сразу скрываются, когда другое окно перекрывает клиент Ragnarok, но остаются на игровом мониторе при работе на втором экране.
- Убрана операция Hide/Show, вызывавшая исчезновение базы после первого клика; Shift, Alt и Ctrl остаются доступными.

## English

- SpringRO item cards show the image, description, NPC sell value, and current server circulation.
- Added advanced monster filters for race, element, size, level, HP, Base/Job EXP, element level, Aggressive, Boss, and MVP.
- The market database refreshes in the background, persists locally, and supports a configurable refresh interval.
- Market listings show a minimap with the merchant position marked in red.
- Overlay visibility now follows the Ragnarok client instead of unrelated windows.

# RoAura 2.10.10

- Удалены персональные стартовые настройки разработчика: адрес RTM, Night Menace, Devil Raid, Harvest и тестовые Skill/Status ID больше не создаются у нового пользователя.
- Чистая установка запускается с пустыми списками Cooldowns и Buffs / Debuffs; пользователь добавляет нужные записи самостоятельно.
- Сборщик релиза перед упаковкой удаляет `settings.json`, пресеты, резервные копии, кеш маркета и логи из каталога публикации.
- После создания portable ZIP выполняется обязательная проверка, что приватные конфигурационные файлы не попали в архив.
- Существующие настройки владельца в `%APPDATA%\RoAura` не удаляются и не изменяются при сборке.

# RoAura 2.10.9

- Окна игровой базы заранее инициализируются при запуске RoAura, чтобы первое открытие вело себя так же стабильно, как повторное.
- При первом клике по полноэкранному клиенту панели базы нативно восстанавливаются поверх игры без изменения логического состояния и без повторной блокировки клавиш-модификаторов.
- Добавлен постоянный кеш маркета SpringRO в `%APPDATA%\RoAura\springro-market-cache.json`.
- Сохранённые результаты отображаются сразу, а устаревшие данные обновляются в фоне каждые пять минут.
- Ошибка или медленный ответ SpringRO больше не удаляет ранее полученные результаты маркета.

# RoAura 2.10.8

- Исправлено исчезновение панелей игровой базы после первого клика по полноэкранному клиенту Ragnarok: панели теперь безопасно возвращаются поверх окна игры без смены фокуса.
- Состояние открытия базы больше не расходится с её видимым состоянием при передаче фокуса игре.
- Привязка через `GWL_HWNDPARENT` не возвращалась, поэтому исправление не должно снова блокировать Alt, Ctrl и Shift.

# RoAura 2.10.7

- По видеозаписи подтверждено, что SpringRO поднимает игровую поверхность в topmost-слой и перекрывает обычный `HWND_TOP`.
- Основной оверлей и окна базы получают `TOPMOST` только пока активно окно Ragnarok; при переходе в другое приложение флаг немедленно снимается.
- Ownership-привязка к окну игры не возвращена, поэтому исправление Alt, Ctrl и Shift сохраняется.

# RoAura 2.10.6

- Исправлено исчезновение всех оверлеев при переключении фокуса на Ragnarok.
- При активном окне RO оверлей поднимается через безопасный `HWND_TOP` без `Topmost`, активации и ownership-привязки к процессу игры.
- Сохраняется исправление Alt, Ctrl и Shift из 2.10.5.

# RoAura 2.10.5

- Восстановлен проверенный механизм горячих клавиш из рабочей версии 2.8.1.
- Оверлей больше не назначается owned-окном процесса Ragnarok через `GWL_HWNDPARENT`; эта новая связь была главным отличием, активировавшимся сразу при запуске и способным конфликтовать с Gepard.
- Оверлей по-прежнему следует координатам окна RO и размещается непосредственно над ним через безопасный Z-order.
- Alt, Ctrl и Shift пропускаются даже при открытой базе данных.

# RoAura 2.10.4

- Исправлено многократное журналирование одного старого пакета `INVENTORY EQUIPMENT 0x0B39` при каждом новом TCP-сегменте.
- Кэш экипировки теперь обновляется только при получении нового снимка инвентаря, а не при повторном просмотре сохранённого хвоста потока.
- Устранён лишний рост `debug.log` и снижена нагрузка пакетного анализатора.

# RoAura 2.10.3

- Служба горячих клавиш полностью переведена с постоянного опроса клавиатуры на стандартный Windows API `RegisterHotKey`.
- RoAura больше не считывает и не перехватывает Alt, Ctrl и Shift ни при запуске, ни при работе оверлеев.
- Подпись базы данных исправлена: активен ввод в поиск, а не глобальная блокировка клавиатуры игры.

# RoAura 2.10.2

- Полностью удалён системный низкоуровневый перехватчик клавиатуры. RoAura больше не может блокировать или оставлять в некорректном состоянии Alt, Ctrl и Shift.
- Настраиваемые горячие клавиши продолжают работать через безопасный опрос состояния клавиш без их захвата.

# RoAura 2.10.1

- Исправлена блокировка клавиш Shift, Ctrl, Alt и Windows при открытом внутриигровом оверлее базы данных.
- Для вкладок «Монстры», «Предметы», «Карты» и «Маркет» сохраняются независимые поисковые запросы.

# RoAura 2.10.0

- Добавлен верхний выбор сервера: RTM, SpringRO и «Другой»; разделы и базы автоматически подстраиваются под выбранный профиль.
- Для SpringRO доступна внутриигровая база; для RTM используются RTM-базы MVP и предметов; профиль «Другой» скрывает серверозависимые разделы.
- Исправлено двойное срабатывание горячей клавиши, из-за которого база могла закрываться сразу после первого открытия.
- Все игровые оверлеи стали дочерними для окна Ragnarok: они остаются над игрой при работе на втором мониторе, но не перекрывают посторонние приложения.
- Убрано повреждение сохранённых координат при временном уменьшении или перезапуске окна клиента.
- Все основные таблицы и таблица статистики получили перетаскиваемую нижнюю границу.
- Добавлен отдельный индикатор расчётного времени до следующего Job Level.

