# Подключение DSM, Semi.Avalonia и кастомизация — 2026-10-17

## Диагностика хранилища при работающих остальных метриках

Пользовательским скриншотом подтверждены реальные CPU/RAM/сеть/температура/uptime
после исправления CSRF (Manually-tested пользователем на его NAS). Хранилище
остаётся «—», статус частичный. Со слов владельца есть один том; это не
доказательство причины отказа. Пользователь сообщил, что tooltip не отображается.

Числовой DSM-код частичного отказа теперь проходит через DsmMetricGroup и
NasMetric в текст метрики. При недоступном хранилище в состоянии Degraded
существующая видимая строка под «Обновить метрики» показывает StorageDetail
с кодом, если DSM его вернул. Сырые ответы/секреты не показываются. Не добавляли
новых storage API, не меняли права, формулы и интервалы опроса.

Implemented / Automated-tested: один том без storagePools даёт 25% для прямого
и вложенного data; код 105 сохраняется только у запрещённого storage;
ViewModel показывает причину и код в MonitorDetail без необходимости tooltip.
Release build: 0 ошибок/предупреждений; полный suite: 105/105 (23 Core +
82 Connection). Windows пакет пересобран командой publish ниже.

Изменены NasMetric.cs, DsmMetricGroup.cs, SynologyMetricsSession.cs,
DsmSnapshotParser.cs, WidgetViewModel.cs, DsmMetricsTests.cs,
MonitoringTests.cs; документация DECISIONS, DSM-MONITORING,
DSM-CONNECTION-TESTING и этот отчёт.

Команды: `dotnet build NasMonitor.slnx -c Release`,
`dotnet test NasMonitor.slnx -c Release --no-build`,
`./scripts/publish.ps1 -Runtime win-x64`.
Причина storage на реальном NAS и GUI новой диагностики — Unverified.
Для продолжения нужно получить видимую строку после запуска нового EXE.
macOS пакеты в этом срезе не пересобирались, сохраняют предыдущую CSRF-версию.

## Исправление CSRF при чтении метрик

Пользователь подтвердил успешную авторизацию после удаления произвольного
session (Manually-tested пользователем для его NAS). Затем на странице виджета
показана ошибка «Сессия истекла или была прервана», без доступных показателей.
Это отдельный сбой после входа; успешность чтения метрик не подтверждена.

В DsmProtocol токен SynoToken передавался только в POST body. Теперь он исключён
из формы и отправляется как X-SYNO-TOKEN в заголовке отдельного запроса по
документации Synology. SID/пароль/OTP остаются в POST body; URL и общие
DefaultRequestHeaders не содержат токен. Не включаем общие cookies и не
отключаем CSRF. Discovery после входа, метрики и logout используют этот транспорт.
WidgetViewModel показывает точный DSM-код рядом с контролируемой ошибкой сессии.

Implemented / Automated-tested: тест CSRF-защищённого сервера выдавал 119
на прежнем запросе, после исправления читает CPU/тома/температуру и закрывает
сессию с заголовком. Проверены отсутствие токена в URL/форме, отсутствие
заголовка до входа и отсутствие переноса токена при повторном входе тем же
HttpClient. Проверен показ DSM 119 в UI ViewModel.

`dotnet build NasMonitor.slnx -c Release`: 0 ошибок и предупреждений.
`dotnet test NasMonitor.slnx -c Release --no-build`: 103/103 (23 Core + 80 Connection).
`./scripts/publish.ps1 -Runtime all`: успешно пересобраны Windows x64 и
macOS arm64/x64 executables, ZIP и SHA256SUMS.txt. GUI этих пакетов в рамках
CSRF-исправления не запускался; Windows ViewModel проверена unit-тестом.
Реальный результат CSRF-исправления и доступ к метрикам с делегированной ролью
остаются Unverified. Блокер подтверждения — повторное подключение пользователя;
новых сетевых обращений к реальному NAS из среды разработки не выполнялось.

В этом исправлении изменены DsmProtocol.cs, WidgetViewModel.cs,
DsmConnectionTests.cs, DsmMetricsTests.cs, MonitoringTests.cs и документация
DECISIONS, SECURITY, DSM-CONNECTION-TESTING, этот отчёт.

## Исправление запроса авторизации при DSM 402

Пользователь воспроизвёл DSM 402 на стадии авторизации при разрешённом приложении
DSM; дополнительно показана делегированная роль «Мониторинг системы». В запросах
обнаружены произвольные имена приложений session: NASMonitorProbe и NASMonitor.
Их удалили из общего AuthParameters и обоих вызовов probe. Теперь login не задаёт
необязательное имя приложения, logout использует полученный `_sid` и SynoToken.
Это вероятная причина наблюдаемого отказа, а не подтверждённый результат на NAS.

Implemented / Automated-tested: два регрессионных сценария сначала падали на
прежних запросах, после исправления все 35 DSM connection/metrics тестов прошли.
Release build: 0 ошибок и предупреждений. Полный suite: 100/100 (23 Core +
77 Connection). Результаты основаны на синтетических HTTP-ответах; реальный NAS
из этой среды не опрашивался. Исправление 402, независимость серверных сессий,
права делегированной роли на каждую метрику и macOS runtime — Unverified.
Блокер окончательного подтверждения — повторный запуск пользователем.

Изменены: Providers.Synology/Protocol/DsmProtocol.cs,
Providers.Synology/Connections/SynologyConnectionTester.cs,
Connection.Tests/Connections/DsmConnectionTests.cs,
Connection.Tests/Monitoring/DsmMetricsTests.cs; документация DECISIONS,
ARCHITECTURE, DSM-MONITORING, DSM-CONNECTION-TESTING и этот отчёт.

Команды: `dotnet build NasMonitor.slnx -c Release`,
`dotnet test NasMonitor.slnx -c Release --no-build`,
`./scripts/publish.ps1 -Runtime all`.
Публикация всех трёх RID завершилась успешно: обновлены EXE Windows,
macOS arm64/x64 executables и ZIP, SHA256SUMS.txt. Первые sandbox-запуски
не завершили восстановление RID-зависимостей; повторная публикация с разрешённым
восстановлением NuGet прошла. Новые пакеты в GUI в рамках этого исправления
не запускались; готовность пакетов не подтверждает подключение к реальному NAS.
Повторная ручная проверка: раздел DSM 402 в DSM-CONNECTION-TESTING.md.

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
