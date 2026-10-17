# Подключение DSM, Semi.Avalonia и кастомизация — 2026-10-17

Актуальный этап реальных метрик: [DSM-MONITORING.md](DSM-MONITORING.md).
Указания ниже о DEMO/отсутствии poller относятся к историческому срезу подключения.

## Исправление формы и single-file

Дополнительно по запросу пользователя NAS перенесены в боковую панель под
вкладками. Общая коллекция автоматически синхронизирует CRUD; выбор, повторный
клик и Enter открывают форму профиля. Дублирующий список на странице удалён.
Windows smoke проверяет синхронизацию выбора, rename/delete и повторное открытие
на synthetic профилях без записи на диск (`artifacts/ui/sidebar-nas.png`).

Implemented: ограничена ширина страниц (виджет 1000, подключения 840 DIP), левое выравнивание,
«Добавить NAS» закреплена внизу справа вне ScrollViewer и остаётся видимой
при прокрутке формы; список устройств оставлен только в sidebar.
Предпросмотр использует независимое
состояние черновика и обновляется от событий всех полей, включая текстовый ввод
чисел без потери фокуса. Частичный HEX сохраняет последний корректный цвет.
Общие метрики продолжают обновляться; Apply отдельно сохраняет и применяет desktop.

Публикация: single-file + native self-extraction, embedded debug information,
без trimming. Windows распространяется как `artifacts/NasMonitor-win-x64.exe`.
Native extraction выполняется .NET runtime; один файл распространяется, а не
обещается отсутствие runtime-cache или пользовательских настроек на диске.

Проверки: build без предупреждений/ошибок, 57 unit/integration tests passed;
Windows smoke проверяет реальные UI events live preview, ширину/левый отступ
на широком окне, закрепление кнопки у правого нижнего края вне ScrollViewer
и создаёт PNG. Финальный Windows EXE запущен из папки с единственным файлом:
smoke завершился с кодом 0. В ZIP находится один EXE.
Реальные DSM/macOS остаются Unverified.
Изменены WidgetSettingsView XAML/code, NasConnectionsView XAML/code,
ManagerWindow code, Strings.resx, scripts/publish.ps1 и документация.

## Объём

После подтверждения Windows drag/resize пользователь явно отложил macOS и
разрешил переход к DSM. Затем попросил Semi.Avalonia, отдельные страницы с
боковой навигацией и кастомизацию виджета. Все три запроса включены в этот срез.
Полная desktop-матрица при этом не считается принятой.

## Статусы

| Требование | Статус | Подтверждение / предел |
|---|---|---|
| Несколько профилей, CRUD, enabled | Implemented / Automated-tested | JSON round-trip, независимые IDs, disable/rename/delete; UI на реальном NAS Unverified |
| Основной и внешний endpoint | Implemented / Automated-tested | HTTPS origins, внешний выбор явно; автоматический fallback отложен |
| DNS/TCP/TLS по этапам | Implemented / Automated-tested | Mock stages + реальный локальный self-signed TLS reject; реальные NAS Unverified |
| API discovery / Auth v6 login/logout | Implemented / Automated-tested | Mocked HTTP, discovered path/version, POST, cleanup после отмены/ошибки |
| OTP / ошибки DSM | Implemented / Automated-tested | Коды 400/401/402/403/404/406/407/409/105; реальная 2FA Unverified |
| Capabilities | Implemented / Automated-tested | Объявленные API после login; наличие API не означает права |
| Минимальные права мониторинга | Unverified | Показано явно, metric API ещё не вызываются |
| Windows Credential Manager | Implemented / Automated-tested | Настоящий synthetic write/read/delete под случайным UUID, cleanup в finally |
| macOS Keychain | Implemented / Unverified | Security.framework SecItem + CF, только cross-build; GUI/entitlements отложены |
| TLS/секреты | Implemented / Automated-tested | Нет credentials в URL, path traversal reject, нет секретов в профилях/результатах, редиректы/cookies/loggers отключены |
| Semi.Avalonia 11.3.22 | Implemented / Automated-tested | Совместимая с Avalonia 11.3.22 версия; реальный Windows launch и render обеих тем |
| Две страницы и sidebar | Implemented / Automated-tested | Отдельные UserControl, единый shell; smoke переключает страницы |
| Очистка пароля/OTP при навигации | Implemented / Automated-tested | Unit-тест, отмена текущей проверки при уходе |
| Кастомизация и предпросмотр | Implemented / Automated-tested | Название, акцент, alpha, углы, тема, CPU/RAM/time; общий WidgetContentView |
| Сохранение внешности | Implemented / Automated-tested | appearance.json round-trip, не изменяет layout.json; границы и невалидные значения |
| Прозрачность на реальном desktop | Unverified | Native API success/PNG не подтверждают OS-композицию после reparent |
| Настоящее подключение DS216+II / DS1821+ | Unverified | Реальные адреса/версии/учётные данные не использовались |
| Постоянный monitoring / реальные CPU/RAM | Unverified / следующий срез | Виджет явно остаётся DEMO, poller не создавался |

## Проверки

- `dotnet build NasMonitor.slnx -c Release`: **0 ошибок, 0 предупреждений**.
- `dotnet test NasMonitor.slnx -c Release`: **57/57 passed** (Core 17, Connection 40).
- Реальные тесты в составе suite: Windows credential round-trip и локальный
  TLS-сервер с недоверенным сертификатом. Ни одного обращения к реальному NAS.
- Windows smoke build 26200: WorkerW attachment, host alive, widget visible,
  synthetic updates; GUI запускается с Semi и переключает обе страницы/темы.
- Render PNG в `artifacts/ui/` проверены визуально на переполнение и читаемость.
  Это visual QA изображения собственного Avalonia tree, не manual-приёмка OS.
- Исходный smoke дополнен кодом возврата из Program.Main: результат Shutdown
  теперь передаётся вызывающему процессу.
- macOS по решению пользователя не запускалась; Keychain/AppKit **Unverified**.
- `./scripts/publish.ps1`: подготовлены self-contained ZIP win-x64, osx-arm64,
  osx-x64 и обновлены `artifacts/SHA256SUMS.txt`. Финальная опубликованная
  Windows-сборка прошла smoke, exit code 0. Подписание macOS не выполнялось.

Команды для воспроизведения: `dotnet build NasMonitor.slnx -c Release`,
`dotnet test NasMonitor.slnx -c Release`, `./scripts/publish.ps1`,
`dotnet artifacts/publish/win-x64/NasMonitor.App.dll --smoke-test`.

## Файлы

- Core: добавлены `NasConnection.cs`, `WidgetAppearance.cs`.
- Добавлен `Providers.Synology`: csproj, `DsmTransportProbe.cs`, `SynologyConnectionTester.cs`.
- Добавлен `Infrastructure`: csproj, `NasProfileRepository.cs`, `NasProfileService.cs`.
- Platform.Windows / Platform.MacOS: добавлены `WindowsSecretStore.cs` /
  `MacSecretStore.cs`, обновлены project references.
- App: обновлены csproj, App XAML/bootstrap, Program, ManagerWindow XAML/code,
  WidgetWindow, WidgetController, SpikeStorage, SpikeViewModel, Strings.resx;
  добавлены WidgetSettingsView, NasConnectionsView, NasConnectionsViewModel,
  WidgetContentView. Промежуточное отдельное окно DSM заменено страницей.
- Tests: Connection.Tests project (DsmConnectionTests, ProfileTests, TlsTests,
  AppearanceStorageTests), Core.Tests/WidgetAppearanceTests.
- Solution, README, ARCHITECTURE, DECISIONS, SECURITY, desktop reports/checklist;
  добавлены task 02, DSM test instructions и этот отчёт.

## Что проверить дальше

1. На обновлённой Windows-сборке проверить sidebar, кастомизацию, перезапуск,
   прозрачность, drag/resize/locked после перехода на Semi.
2. Ввести адрес DSM и учётные данные в приложении, выполнить login/OTP/logout,
   сохранить фактическую DSM version/модель/права и номера ошибок без секретов.
3. Проверить второй NAS и недоступность одного из них. Выполнить протокол из
   `DSM-CONNECTION-TESTING.md`. Текущая реализация не делает авто-retry логина.
4. Следующим срезом исследовать доступные read-only metric API и минимальные права,
   затем реализовать одну службу polling на NAS. Не снимать Unverified заранее.
5. macOS desktop/Keychain и OS lifecycle остаются техническим долгом, отложенным
   пользователем, а не подтверждённой совместимостью.

## Источники реализации

- [DSM Login Web API Guide, Synology](https://global.download.synology.com/download/Document/Software/DeveloperGuide/Os/DSM/All/enu/DSM_Login_Web_API_Guide_enu.pdf)
- [Semi.Avalonia repository](https://github.com/irihitech/Semi.Avalonia)
- [Semi.Avalonia 11.3.22, NuGet dependencies](https://www.nuget.org/packages/Semi.Avalonia/11.3.22)
- [IHttpClientFactory, .NET](https://learn.microsoft.com/dotnet/core/extensions/httpclient-factory)
- [Credential Manager CREDENTIALW](https://learn.microsoft.com/windows/win32/api/wincred/ns-wincred-credentialw)
- [Apple generic-password item keys](https://developer.apple.com/documentation/security/ksecclassgenericpassword)
