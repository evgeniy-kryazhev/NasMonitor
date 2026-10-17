# Архитектура NAS Monitor

## Реализованный срез desktop spike

`NasMonitor.slnx`: Core (геометрия/синтетические samples), Hosting (контракт без UI),
Platform.Windows, Platform.MacOS, App (Avalonia UI, MVVM, DI, lifecycle и JSON),
Core.Tests (MSTest). Ниже — целевая структура следующих этапов, не перечень уже
созданных компонентов. Причины упрощения: `DECISIONS.md`.
По явному решению пользователя после Windows-проверок начат этап подключения DSM;
macOS desktop-приёмка отложена, её статус Unverified.

## Реализованный срез подключения DSM

Core содержит NasProfile, контролируемые результаты проверки, INasConnectionTester
и ISecretStore. Providers.Synology использует typed HttpClient через HttpClientFactory,
IDsmTransportProbe, discovery, Auth v6 login/logout и объявленные API. Одна проверка
создаёт одну временную сессию; её SID/CSRF не выходят из provider.
Infrastructure хранит версионированный JSON профилей и координирует OS secrets.
WindowsSecretStore находится в Platform.Windows, MacSecretStore — в Platform.MacOS.

App использует Semi.Avalonia 11.3.22. ManagerWindow — оболочка с боковым ListBox
и ContentControl, WidgetSettingsView и NasConnectionsView — независимые страницы.
Состояние формы сохраняется при переключении, секретные поля очищаются, активная
проверка отменяется. ViewModel подключения не знает о HTTP, HWND или Keychain.
Desktop widget и его lifecycle продолжают управляться WidgetController.

## Реализованный мониторинг DSM

Core определяет NasMetric, NasSnapshot, NasVolume, NasMonitorState и контракты
сессии. Providers.Synology содержит общий DsmProtocol и MetricsSessionFactory:
API discovery, вход, чтение Utilization/System/Storage и ограниченный по времени
logout. Пароль/OTP передаются только при входе; SessionId/SynoToken живут внутри
provider и не попадают в snapshot. Проба использует отдельное имя сессии.

Infrastructure/NasMonitoringCoordinator владеет одним sequential loop на ID NAS,
общим immutable-кэшем, глобальным лимитом трёх одновременных циклов запросов,
backoff+jitter, отменой и повторным входом из OS store. Пересохранение изменённого
профиля перезапускает только его loop; удаление/отключение завершает сессию.
Utilization опрашивается каждые 10 секунд после завершения цикла; System/Storage
кэшируются 60 секунд со своими timestamps. Ручные refresh coalesce и обновляют
все группы. Сетевой сбой сохраняет последний snapshot с явным stale-состоянием.

WidgetMonitoringPresenter переводит события на UI dispatcher, синхронизирует
список источников и сохраняет выбранный ID в DesktopSpike/source.json.
SpikeViewModel теперь показывает реальные данные; synthetic stream включается
только флагом smoke. Окна и предпросмотр не создают polling-loop.
WidgetContentView измеряет тот же контент для минимальной высоты desktop-окна;
native host получает max(запрошенная высота, контент) с ограничением рабочей областью.
Размер можно увеличивать вручную; старое поле AutoHeight игнорируется.
WidgetSettingsView разделён на вкладки внешности и расположения; источник NAS общий.

## Модули
```
NasMonitor.sln
src/
  NasMonitor.App/                 # Avalonia shell, DI bootstrap, settings
  NasMonitor.Core/                # value objects, NasSnapshot, enums, interfaces
  NasMonitor.Application/         # orchestration, monitoring services, state store
  NasMonitor.Infrastructure/      # config, secrets abstraction, HTTP, persistence
  NasMonitor.Providers.Synology/  # DSM API discovery/auth/system/resource/storage
  NasMonitor.Widgets/             # Avalonia widget windows, view models, UI
  NasMonitor.Platform.Windows/    # Win32 desktop hosting
  NasMonitor.Platform.MacOS/      # AppKit desktop hosting
 tests/
  NasMonitor.Core.Tests/
  NasMonitor.Providers.Synology.Tests/
  NasMonitor.Application.Tests/
```
Упростить структуру допустимо только при сохранении границ ответственности.

## Главные контракты (логические эскизы, не готовый compile-ready API)
```
INasProvider:
  ConnectAsync(profile, ct) -> ConnectionResult
  GetCapabilitiesAsync(ct) -> NasCapabilities
  GetSnapshotAsync(ct) -> NasSnapshot
  DisconnectAsync(ct)

IMonitoringCoordinator:
  Subscribe(nasId) -> stream of NasState
  ForceRefreshAsync(nasId, ct)

IDesktopWidgetHost:
  AttachAsync(widgetWindow, ct) -> DesktopAttachResult
  DetachAsync(widgetWindow, ct)
  RestoreAsync(widgetWindow, ct)
  GetCapabilities() -> DesktopHostCapabilities

ISecretStore:
  SaveAsync(key, secret, ct)
  ReadAsync(key, ct)
  DeleteAsync(key, ct)
```
Не таскать Avalonia `Window` в `Core`: передать window handle/host-specific adapter через Platform layer, а интерфейс в UI/hosting assembly.

## Dataflow
DSM HTTP -> Synology Provider -> Normalized Snapshot -> MonitoringCoordinator -> StateStore -> ViewModels -> Avalonia Widget Views / Main App.

## Состояния
Каждый NAS хранит независимые `ConnectionStatus`, `LastSuccessAt`, `LastAttemptAt`, `ErrorCode`, `NasSnapshot?`. Для метрик использовать nullable value и availability enum. Никаких магических 0 как признака недоступности.

## Получение метрик
У одного NAS один асинхронный polling-loop. Периоды для resource и storage различаются; ограничения числа HTTP запросов; отмена при удалении NAS; backoff после сетевых ошибок; обновление токена/сессии согласно DSM. Избегать длительного хранения логина/пароля в памяти сверх необходимого.

## UI
CommunityToolkit.Mvvm, ViewModels без прямого доступа к HTTP, Win32 или AppKit. Окна виджетов создаются через WidgetManager, привязаны к NasId либо агрегату. Позиции сохраняются с идентификатором дисплея и безопасным fallback, если монитор пропал.

## Платформенные модули
Windows: доступ к HWND; эксперимент с Explorer desktop-host, lifecycle и shell-restart. Mac: доступ к NSWindow/AppKit, window levels и spaces behavior. Не считать одинаковыми по контракту системные свойства двух ОС.

## Delivery
Приоритет разработки: platform spike -> skeleton/connection -> telemetry -> widget presentation -> packaging. CI может собирать код без подтверждения фактического GUI поведения на конкретной ОС.
