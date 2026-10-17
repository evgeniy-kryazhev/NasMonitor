# Ручная проверка desktop spike

## Запуск сборок

Пакеты self-contained: установленный .NET на тестовой машине не нужен.
Архивы находятся в `artifacts/NasMonitor-spike-<rid>.zip`.
Windows: распаковать **весь** `win-x64`, запустить `NasMonitor.App.exe`.
Запускать обычным пользователем в интерактивной сессии с Explorer.
В Manager виден код host. При отказе виджет скрыт — это ожидаемая диагностика,
а не доказательство desktop mode. Для повторной попытки есть «Повторить host».

macOS: выбрать osx-arm64 для Apple Silicon или osx-x64 для Intel.
Распаковать `NAS Monitor.app`. Архив подготовлен на Windows, поэтому до запуска:

```sh
chmod +x "NAS Monitor.app/Contents/MacOS/NasMonitor.App"
open "NAS Monitor.app"
```

Сборки не подписаны и не notarized. Для локального теста доверенной сборки
может потребоваться разрешение в System Settings → Privacy & Security → Open Anyway.
Не отключайте Gatekeeper глобально. Подписание для распространения — следующий
отдельный этап. Альтернатива запуску bundle — собрать из исходников на Mac:

```sh
dotnet run --project src/NasMonitor.App -c Release
dotnet publish src/NasMonitor.App -c Release -r osx-arm64 --self-contained true -o artifacts/local-mac
```

Для Intel заменить RID на `osx-x64`. Для сборки из исходников нужен .NET 10 SDK.
Manager остаётся в taskbar/Dock; закрытие Manager завершает процесс и виджет.
Меню-бар/tray в этом spike не реализованы.

## Управление

- CPU/RAM меняются каждые 2 секунды; данные только синтетические.
- Задать X/Y (физические экранные пиксели), ширину/высоту (DIP), снять locked,
  нажать «Применить и сохранить». Отрицательные X/Y допустимы для мониторов слева/сверху.
- Нажать «Перемещать виджет», затем зажать левую кнопку на любой области виджета
  и перетащить. Позиция сохраняется при отпускании, X/Y в Manager обновляются.
  «Завершить перемещение» фиксирует виджет. Снятие locked + Apply также включает drag.
  Размер меняется за нижний правый уголок (24 DIP), видимый только в edit-режиме,
  или через поля Manager. Resize сохраняет верхний левый угол, ограничен рабочей
  областью и пределами 260–720 × 190–600 DIP (на маленьком экране может быть меньше).
- Проверить resize: рост/уменьшение, границы размера, сохранение после перезапуска,
  locked, дисплеи 100/150/200%, отсутствие перемещения верхнего левого угла.
- Проверить перетаскивание без скачка в начале, отпускание за границами виджета,
  перенос между дисплеями 100/150/200%, края экрана, перезапуск с сохранённой позицией.
  В locked жест не должен двигать виджет; при потере capture drag завершается.
  На скриншоте виджет перекрывается desktop-иконками: для проверки начинать жест
  на свободной от иконок области. Shell может перехватывать ввод — это manual-проверка.
- Locked включает запрет ввода в native host. На Windows click-through WS_EX_TRANSPARENT
  экспериментален и требует отдельной проверки; его наличие не доказывает сквозной ввод.
- Переключить темы: системная, светлая, тёмная. Тема пока не сохраняется.
- Настройки: `LocalApplicationData/NasMonitor/DesktopSpike/layout.json`.
  Windows обычно `%LOCALAPPDATA%\NasMonitor\DesktopSpike`; macOS .NET обычно использует
  `~/.local/share/NasMonitor/DesktopSpike`. Точный путь зависит от окружения.
- Рядом `spike.log` и один ротированный `spike.previous.log`, только event codes
  и типы исключений, без HWND, имён пользователей, адресов NAS или паролей.
- Для сброса расположения при закрытом приложении переименовать `layout.json`.

## Протокол

Для каждой машины записать OS build, CPU architecture, дисплеи/scale,
версию Explorer/Finder, результат, наблюдения и при возможности скринкаст.
«Native API: применено» ещё не означает прохождение теста.
Заполнять отдельный результат для Windows 10, Windows 11, macOS arm64 и macOS x64.

| Проверка | Ожидаемый результат | Win10 | Win11 | Mac arm64 | Mac x64 |
|---|---|---|---|---|---|
| Запуск | Manager + виджет; CPU/RAM и время меняются | Unverified | Unverified | Unverified | Unverified |
| Перекрытие | Несколько обычных окон закрывают виджет | Unverified | Unverified | Unverified | Unverified |
| Клик locked/edit | Виджет не поднимается над приложениями | Unverified | Unverified | Unverified | Unverified |
| Show Desktop | Win+D / macOS Show Desktop оставляет виджет видимым | Unverified | Unverified | Unverified | Unverified |
| Иконки desktop | Видны, доступны; не закрыты фоном виджета | Unverified | Unverified | Unverified | Unverified |
| Taskbar/Dock | Нет отдельной кнопки виджета; Manager допустим | Unverified | Unverified | Unverified | Unverified |
| Геометрия | Apply, restart приложения сохраняют положение/размер/lock | Unverified | Unverified | Unverified | Unverified |
| Pointer drag | Edit: движение ЛКМ и сохранение; locked: запрет; capture loss: завершение | Unverified | Unverified | Unverified | Unverified |
| Pointer resize | Edit: уголок меняет размер, locked: уголка нет; размер сохраняется | Unverified | Unverified | Unverified | Unverified |
| Два монитора | Перенос через координаты, корректный scale 100/150/200% | Unverified | Unverified | Unverified | Unverified |
| Отключение монитора | За ≤ 4 с возврат в рабочую область доступного дисплея | Unverified | Unverified | Unverified | Unverified |
| DPI/resolution | Виджет остаётся видим, размер и текст корректны | Unverified | Unverified | Unverified | Unverified |
| Shell restart | После штатного restart Explorer / relaunch Finder восстановление | Unverified | Unverified | Unverified | Unverified |
| Сон/пробуждение | Обновления и host восстанавливаются, нет дубликатов | Unverified | Unverified | Unverified | Unverified |
| Virtual desktops | Смена desktop; записать фактическую видимость | Unverified | Unverified | — | — |
| Spaces/Mission Control | Записать видимость и отсутствие подъёма над приложениями | — | — | Unverified | Unverified |
| Fullscreen/Stage Manager | Проверить Show Desktop и возврат из fullscreen | — | — | Unverified | Unverified |
| Новый вход | Ручной запуск после входа; автозапуска ещё нет | Unverified | Unverified | Unverified | Unverified |
| Завершение | Закрытие Manager убирает все окна/процесс | Unverified | Unverified | Unverified | Unverified |

Shell restart выполнять после сохранения пользовательской работы штатными средствами
ОС; приложение не перезапускает Explorer/Finder автоматически.

### Подтверждение пользователя 2026-10-17

На своей Windows пользователь подтвердил: перетаскивание, перекрытие обычными
окнами, Win+D, фиксацию и сохранение позиции после перезапуска — **Manually-tested**.
Матрица выше остаётся для отдельных OS/build-конфигураций: пользователь не указал
build тестового запуска. Это подтверждение не относится к capture loss, resize,
фильтрации иконок, нескольким мониторам, DPI, shell restart или macOS.

## Команды разработчика

```powershell
dotnet build NasMonitor.slnx -c Release
dotnet test NasMonitor.slnx -c Release
./scripts/publish.ps1
./scripts/publish.ps1 -Runtime win-x64
dotnet run --project src/NasMonitor.App -c Release -- --smoke-test
```

Smoke запускается на 8 секунд, сам завершается и записывает
`artifacts/smoke-report.json` относительно текущей директории. Проверяет API и
обновления, **не** выполняет визуальные сценарии таблицы.
Код выхода 0 означает живой host, видимый/непрозрачный виджет и обновление метрик;
при отказе возвращается 1. JSON нужно сохранять вместе с результатами проверки.

## Известные ограничения и дальнейшее исследование

WorkerW зависит от версии/режима Explorer. При отсутствии host нет автоматического
перехода к обычному плавающему окну. Если на интерактивном Windows обнаружен отказ,
следующий шаг — исследовать реальную иерархию shell на этой версии и записать
в DECISIONS альтернативный desktop host. Не размещать окно под Progman вслепую.
SetParent между процессами имеет DPI ограничения; требуется матрица разных scale.
При уничтожении child HWND Explorer восстановление Avalonia window не проверено.
На macOS desktop level+1 может взаимодействовать с Finder icons, Show Desktop,
Spaces и Stage Manager иначе на разных версиях — это блокирующая ручная проверка.
Нет обещания универсальной поддержки этих сценариев на основании одного вызова API.
Сохранённая геометрия пока использует координаты, а не устойчивый display ID.
Повторная привязка одинакового расположения после перестановки мониторов требует
будущего display identity mapping. Один виджет достаточен для spike, несколько — позже.

2026-10-17 пользователь подтвердил resize на Windows и явно разрешил начать DSM,
отложив macOS. Остальные непроверенные desktop-сценарии остаются Unverified.
