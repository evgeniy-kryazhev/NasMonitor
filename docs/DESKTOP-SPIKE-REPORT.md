# Отчёт desktop spike — 2026-10-17

## Обновление по результатам пользователя

Пользователь подтвердил resize на своей Windows — **Manually-tested**.
Затем явно разрешил отложить macOS и продолжить подключение DSM. Это разрешение
снимает запрет перехода к следующему этапу; macOS/DPI/lifecycle остаются Unverified.
Текущий этап и результаты — `DSM-CONNECTION-REPORT.md`.
Исторические блокеры и статусы ниже описывают состояние до этого решения.

## Текущий срез: ручная проверка Windows и pointer resize

**Manually-tested (пользователем):** drag, перекрытие обычными окнами, Win+D,
фиксация и сохранение позиции после перезапуска на его Windows. Build запуска
пользователем не указан, поэтому результат не переносим на всю OS-матрицу.

**Implemented:** изменение размера за нижний правый уголок в edit-режиме,
ограничение размера рабочей областью без сдвига начала, сохранение при отпускании.
В locked уголок скрывается. Повторно используется существующий desktop-host input.

**Automated-tested:** `dotnet build NasMonitor.slnx -c Release` — 0 ошибок и
предупреждений; `dotnet test NasMonitor.slnx -c Release --no-restore` — **13/13**.
Пять новых resize-тестов проверяют DPI 150%, отрицательный X, границы экрана,
минимум/максимум, возврат от предела, исчезновение дисплея и grip/locked hit-test.
GUI smoke обнаружил неверное имя курсора в XAML; исправлено на BottomRightCorner.
Повторный Windows build 26200 smoke: 3 updates, WorkerW attached, host alive,
widget visible. Smoke теперь возвращает 1 при отказе host/виджета/метрик.

Изменены: `App.axaml.cs`, `WidgetController.cs`, `SpikeViewModel.cs`, `WidgetWindow.axaml`,
`Strings.resx`, README, DECISIONS, TESTING, REPORT.
Добавлены: `Core/WidgetResizeSession.cs`, `Core.Tests/WidgetResizeTests.cs`.

**Unverified:** ручной resize и сохранение нового размера, DPI/несколько дисплеев,
shell restart, сон/пробуждение, macOS GUI. Эти проверки, включая приёмку macOS,
остаются блокером перехода к DSM по AGENTS.md. Инструкции — в TESTING.

## Предыдущий срез: режим перемещения курсором

Implemented: кнопки «Перемещать виджет» / «Завершить перемещение», drag в edit,
фиксация, обновление X/Y и сохранение при отпускании. На macOS — Avalonia capture;
на Windows — опрос указателя в edit с проверкой shell/иконок через accessibility.
WorkerW parent и desktop Z-order сохраняются. Resize остаётся через Manager.

Automated-tested: Release build без предупреждений/ошибок; MSTest **8/8 passed**,
включая 3 новых теста переноса между дисплеями с разным DPI/отрицательными X,
возврата от края без накопления ошибки и отключения дисплея во время жеста.
Windows build 26200 smoke обновлённого self-contained приложения: 3 updates,
`windows.workerw-attached-experimental`, host alive, widget visible, exit code 0.
В sandbox первый smoke получил native-error-5; интерактивный запуск вне sandbox
прошёл. Пересобраны self-contained архивы win-x64/osx-arm64/osx-x64.

Unverified: фактическое перетаскивание мышью, отсутствие выделяющей рамки Explorer
во время жеста, фильтрация иконок, disk round-trip после drag, DPI и OS lifecycle.
macOS GUI не запускался. Ручная приёмка desktop spike остаётся блокером следующего
этапа. Инструкция: `DESKTOP-SPIKE-TESTING.md`.

Изменены: App/WidgetController, WidgetWindow (XAML/code-behind), ManagerWindow
(XAML/code-behind), Strings.resx, Hosting/IDesktopWidgetHost,
Platform.Windows/WindowsDesktopHost, docs/DECISIONS, TESTING, REPORT и README.
Добавлены Core/WidgetDragSession и Core.Tests/WidgetDragTests.

Команды: `dotnet build NasMonitor.slnx -c Release`,
`dotnet test NasMonitor.slnx -c Release`, `./scripts/publish.ps1`,
`dotnet artifacts/publish/win-x64/NasMonitor.App.dll --smoke-test`.

Реализован запускаемый прототип. **Полная платформенная приёмка не завершена**:
часть Windows-сценариев подтверждена пользователем; macOS не проверена.
Следующий этап DSM не начат.

## Фактические проверки

- SDK 10.0.401; net10.0, Nullable, Avalonia **11.3.22** — последний stable 11.x,
  проверенный по NuGet API 2026-10-17. CommunityToolkit.Mvvm 8.4.2, DI 10.0.12.
- `dotnet build NasMonitor.slnx -c Release`: успех, 0 предупреждений, 0 ошибок.
- `dotnet test NasMonitor.slnx -c Release --no-build --no-restore`: **5/5 passed**.
  Проверены отключённый дисплей, отрицательные координаты второго дисплея,
  неверные размеры/маленький экран, отсутствие screen data, диапазоны synthetic samples.
- Автоматический реальный GUI-запуск на Windows x64, OS version `10.0.26200`.
  За 8 секунд: 3 synthetic updates, `windows.workerw-attached-experimental`,
  `NativeHostAlive=true`, `WidgetVisible=true`; штатный выход, exit code 0.
  Исходный отчёт: `artifacts/smoke-report.json`.
- Self-contained Windows executable также запущен с `--smoke-test`.
  Проверен состав всех трёх ZIP: application, runtime и native libraries присутствуют;
  SHA256 записаны в `artifacts/SHA256SUMS.txt`.
- Первый запуск выявил NRE именованных XAML controls: исправлено использованием
  generated InitializeComponent. Следующие запуски прошли.
- Классический top-level WorkerW не найден. Read-only диагностика выявила nested
  WorkerW под Progman; добавленный путь успешно прошёл native smoke.
- Перекрытие окнами, Win+D, drag, фиксация и сохранение позиции подтверждены
  пользователем на своей Windows. DPI и shell restart **не проверены**.
- macOS arm64/x64: только cross-publish; AppKit и GUI **not tested**.

## Статусы требований задачи 01

| Требование | Статус | Граница подтверждения |
|---|---|---|
| Решение .NET 10, Avalonia, DI, MVVM | Implemented / Automated-tested | Компиляция + Windows startup |
| NAS name/status/CPU/RAM/last-update, без DSM | Implemented / Automated-tested | Samples + smoke; визуальная читабельность Unverified |
| Независимый desktop-host contract | Implemented | Hosting не зависит от Avalonia; native code в отдельных проектах |
| Windows desktop placement | Implemented / Automated-tested / Manually-tested (частично) | Smoke 26200; пользователь: перекрытие окнами и Win+D. Остальные OS-кейсы Unverified |
| macOS desktop placement | Implemented / Unverified | AppKit desktop level; ни одного Mac запуска |
| Ошибка host без плавающего fallback | Implemented / Automated-tested | workerw-unavailable в первом smoke, виджет скрыт, Manager жив |
| Manager | Implemented / Automated-tested | Создание и штатное завершение в Windows smoke |
| Edit/locked | Implemented / Manually-tested (Windows) | Пользователь подтвердил фиксацию; native click-through и другие ОС Unverified |
| Перемещение/размер/сохранение | Implemented / Unverified | Через поля Manager; геометрия Automated-tested, disk round-trip manual |
| Pointer drag | Implemented / Automated-tested / Manually-tested (Windows) | Пользователь подтвердил движение и сохранение позиции; capture loss и другие ОС Unverified |
| Pointer resize | Implemented / Automated-tested / Unverified | Логика размера покрыта 5 тестами; реальный pointer resize требует manual |
| Восстановление при потере host | Implemented / Unverified | Таймер 2 с, пересоздание окна; Explorer restart manual |
| DPI/дисплеи/resume | Implemented / Unverified | Геометрия tested, OS lifecycle manual |
| Настройки без секретов и диагностические event codes | Implemented | В коде нет DSM/учётных данных, runtime отказ host записан |
| RU и готовность к EN | Implemented | .resx + ResourceManager, для EN добавить Strings.en.resx |
| Системная/светлая/тёмная тема | Implemented / Unverified | FluentTheme; ручная визуальная проверка нужна |
| Протокол OS/version/features | Implemented | DESKTOP-SPIKE-TESTING.md; подтверждение пользователя отдельно от build-матрицы |
| Windows x64 package | Implemented | Self-contained publish + zip |
| macOS arm64/x64 packages | Implemented / Unverified | Self-contained cross-publish, unsigned .app, not tested |

**Manually-tested** относится только к перечисленным пользователем Windows-сценариям.
API success и Window.IsVisible сами по себе не подтверждают desktop-поведение.

## Изменённые/созданные файлы

- Корень: `NasMonitor.slnx`, `Directory.Build.props`, `global.json`, `.gitignore`, `README.md`.
- `src/NasMonitor.Core/`: csproj, `WidgetLayout.cs` — координаты, display area, samples.
- `src/NasMonitor.Hosting/`: csproj, `IDesktopWidgetHost.cs` — native contract и outcomes.
- `src/NasMonitor.Platform.Windows/`: csproj, `WindowsDesktopHost.cs` — Explorer attachment.
- `src/NasMonitor.Platform.MacOS/`: csproj, `MacDesktopHost.cs` — публичные AppKit calls.
- `src/NasMonitor.App/`: csproj, `Program.cs`, `App.axaml[.cs]`,
  `ManagerWindow.axaml[.cs]`, `WidgetWindow.axaml[.cs]`, `SpikeViewModel.cs`,
  `SpikeStorage.cs`, `WidgetController.cs`, `TextExtension.cs`, `Strings.resx`.
- `tests/NasMonitor.Core.Tests/`: csproj, `LayoutTests.cs`.
- `scripts/`: `publish.ps1`, `Info.plist`, `inspect-shell.ps1`.
- `docs/`: обновлён `ARCHITECTURE.md`; созданы `DECISIONS.md`,
  `DESKTOP-SPIKE-TESTING.md`, этот отчёт.
- `artifacts/`: архивы трёх RID, publish directories, macOS bundles,
  `SHA256SUMS.txt`, `smoke-report.json` (generated, исключены из Git).

На момент текущего среза Git показывает исходные файлы как untracked;
commit в рамках этой работы не создавался.

## Блокеры и следующие действия

1. Пользователь выполняет матрицу Windows 10/11 и macOS arm64/x64 из инструкции.
2. Особое внимание: Windows WorkerW зависит от Explorer; nested hierarchy пока
   подтверждена только API. Исследовать клики, иконки, Win+D, shell restart, DPI.
3. На Mac подтвердить native handle, desktop level, Finder, Spaces, fullscreen,
   Mission Control и Stage Manager. Наличие сборки не подтверждает совместимость ОС.
4. Сохранение monitor identity, несколько виджетов,
   трей/menu-bar и подписанные installer packages остаются последующими работами.
5. После результатов приёмки зафиксировать обнаруженные ограничения/исправления
   в DECISIONS; только затем переходить к DSM.

Подробные команды сборки, запуска и ручного тестирования — `DESKTOP-SPIKE-TESTING.md`.
