# Обновления из GitHub Releases

На странице «Обновления» отображаются текущая и доступная версии, результат
проверки и текст релиза. Новая версия отмечается точкой в боковом меню.
По умолчанию проверяем через 5 секунд после запуска, затем раз в 6 часов;
флажок позволяет отключить автоматическую проверку. Выбор сохраняется.
Ручная проверка доступна независимо от этого флажка.

Установка выполняется кнопкой «Обновить и перезапустить»: загрузка с прогрессом,
проверка SHA-256, подготовка helper, завершение приложения, замена executable,
повторный запуск. Загрузку можно отменить. Настройки виджетов, профили NAS и
системное хранилище паролей не заменяются. При отказе сети старое приложение
продолжает работать. Права администратора автоматически не запрашиваются.

## Источник и доверие

Публичный репозиторий: `evgeniy-kryazhev/NasMonitor`.
Используем [официальный latest release API](https://docs.github.com/en/rest/releases/releases#get-the-latest-release)
и [метаданные assets, включая SHA-256 digest](https://docs.github.com/en/rest/releases/assets#get-a-release-asset).
NAS HttpClient и его авторизация не участвуют в запросах обновления.
GitHub-токен в приложение не встраивается; приватные релизы не поддерживаются.

Репозиторий проверен 17 октября 2026: публичный API репозитория отвечает 200,
а `/releases/latest` — 404. Это показывается как недоступность публичных релизов,
а не как подтверждение актуальности установленной версии.

Поддерживаем stable-теги `vMAJOR.MINOR.PATCH`, без prerelease и downgrade.
Выбираем точное имя файла для архитектуры запущенного процесса:

| Платформа | Asset |
| --- | --- |
| Windows x64 | NasMonitor-win-x64.exe |
| macOS arm64 | NasMonitor-osx-arm64 |
| macOS x64 | NasMonitor-osx-x64 |

При отсутствии подходящего файла или проверяемой суммы установка недоступна.
Используем `digest=sha256:…` из API либо точную строку `SHA256SUMS.txt` того же
релиза. Загрузка ограничена размером из метаданных и максимумом 512 MiB.
Повреждённые, неполные и отменённые загрузки не становятся installable payload.
Контрольная сумма проверяет целостность. Доверие к издателю опирается на HTTPS
и права публикации репозитория; SHA-256 не заменяет code signing.

## Установка

Обновление доступно в опубликованной portable single-file сборке, а не при
запуске из `dotnet`/IDE. Каталог приложения должен быть доступен для записи;
доступ проверяется до завершения процесса. Staging расположен в
`%LOCALAPPDATA%/NasMonitor/Updates/` (на macOS — LocalApplicationData).
В нём хранятся только update preferences, бинарники и несекретный install job.

Helper — отдельная копия текущего executable, запущенная с `--apply-update` до
инициализации Avalonia. Он проверяет staged SHA-256 и точный процесс-родитель
(PID, время запуска, путь), сигнализирует готовность и ждёт его завершения.
После повторной проверки обоих файлов создаёт соседний temporary и переносит
прежнюю версию в `.backup-<guid>`. При ошибке замены или запуска выполняется
восстановление. Backup остаётся для ручного восстановления; после успешной
проверки новой версии его можно удалить. Автоотката после последующего падения
уже запущенного приложения нет.

На macOS сохраняем executable mode, заменяем bundled executable и обновляем
версии в Info.plist. Текущие unsigned bundles поддержаны кодом, но не проверены
на macOS. Для подписанных bundles self-update отключён: используйте страницу
релиза, чтобы не нарушать bundle signature. Установка подписанных/notarized
пакетов потребует отдельного механизма, например Sparkle.

## Подготовка релиза

```powershell
dotnet build NasMonitor.slnx -c Release
dotnet test NasMonitor.slnx -c Release --no-build
./scripts/publish.ps1 -Runtime all -Version 0.1.1
./scripts/test-updater.ps1
```

Скрипт создаёт три bare executables, три ZIP и SHA256SUMS.txt; версии assembly
и Info.plist получают переданное значение. macOS ZIP сохраняет Unix creator OS
и executable mode даже при cross-publish на Windows. Обычная Windows-сборка:
`./scripts/publish.ps1 -Runtime win-x64`.

Workflow `.github/workflows/release.yml` работает при push stable-тега.
Он тестирует код (исключая OS Credential Manager integration), собирает все
RID, загружает assets в draft и только затем публикует release как latest.
Actions закреплены проверенными commit SHA. Для запуска канала workflow должен
быть закоммичен в репозиторий, после чего нужно отправить нужный stable-тег.
GitHub Releases/теги в этой задаче не создавались и не отправлялись.

## Проверки и ограничения

| Проверка | Статус |
| --- | --- |
| Версии, точный RID, 404/rate limit, URL и checksum | Automated-tested, fake HTTP |
| Отмена, повреждение и неполная загрузка | Automated-tested |
| Замена файлов, backup и rollback | Automated-tested |
| Helper: ожидание процесса, замена и запуск новой версии | Automated-tested, Windows fixture |
| Страница обновлений, темы и существующие виджеты | Automated-tested, Windows UI smoke |
| Windows/macOS release assets, manifest и Unix ZIP metadata | Automated-tested на Windows |
| Настоящее обновление через опубликованный GitHub Release | Unverified: релизов пока нет |
| Исполнение helper и установка на macOS | Unverified |
| Запуск workflow на GitHub | Unverified: workflow ещё не отправлен |

Release build без предупреждений, все 99 unit-тестов прошли. Интеграционный
`scripts/test-updater.ps1` заменяет только копию приложения в отдельной папке
artifacts, включая пути с пробелами. Новая тестовая программа пишет marker и
завершается; реальные NAS и пользовательские настройки не используются.
Фактический отчёт — `artifacts/update-install-smoke.json`, все его флаги true.

Основные файлы: `Core/Updates/`, `Infrastructure/Updates/`,
`App/ViewModels/UpdatesViewModel.cs`, `App/Persistence/UpdatePreferencesStore.cs`,
`App/Views/Pages/UpdatesView.axaml*`, composition/lifecycle, publish script,
release workflow и тесты `Updates/`.
