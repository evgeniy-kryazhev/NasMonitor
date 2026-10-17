# Стиль кода и рефакторинг

Актуальная документация JetBrains ReSharper 2026.2 описывает настраиваемые
правила команды, а не единый обязательный стиль. Источники:
[EditorConfig](https://www.jetbrains.com/help/resharper/Using_EditorConfig.html),
[Naming style](https://www.jetbrains.com/help/resharper/Coding_Assistance__Naming_Style.html),
[CleanupCode](https://www.jetbrains.com/help/resharper/CleanupCode.html),
[InspectCode](https://www.jetbrains.com/help/resharper/InspectCode.html).

Правила репозитория находятся в `.editorconfig`: Allman braces, обязательные
блоки условий и циклов, file-scoped namespaces, явные модификаторы доступа,
camelCase для приватных полей, PascalCase для констант. `var` применяется,
когда тип очевиден из выражения. Не сжимаем независимые операции в одну строку.
CLI JetBrains.ReSharper.GlobalTools 2026.2.3.1 закреплён в `dotnet-tools.json`.

Файлы распределены по тематическим папкам во всех проектах, пространства имён
соответствуют путям. Карта папок находится в `ARCHITECTURE.md`.
XAML и code-behind, а также дополнительные partial-файлы хранятся рядом.

```powershell
dotnet tool restore
./scripts/check-code.ps1 -Cleanup
dotnet build NasMonitor.slnx -c Release
dotnet test NasMonitor.slnx -c Release --no-build
```

Без `-Cleanup` скрипт только запускает анализ, сохраняя XML в artifacts.
InspectCode возвращает отчёт инспекций; успешный exit code сам по себе
не означает отсутствие замечаний. Не отключаем инспекции глобально ради чистого отчёта.

В рефакторинге от 17 октября 2026:

- Контракты Core/Hosting разделены по файлам типов.
- SpikeViewModel/SpikeStorage переименованы в WidgetViewModel/WidgetSettingsStore.
  Существующие каталоги DesktopSpike и формат JSON сохранены.
- Регистрация DI вынесена в ApplicationServices, DSM-сессия отделена от фабрики.
- Диагностические UI-сценарии вынесены в partial-файлы SmokeTests.
- Native declarations и interop helpers отделены от основных классов hosts/secret stores.
  Сигнатуры P/Invoke и порядок полей native-структур сохранены.
- Валидация RGB общая для хранения и редактора. Штатный Avalonia ColorPicker
  использует Semi-тему и синхронизируется с HEX и черновиком предпросмотра.

Проверки и ограничения: Release build и 80 unit-тестов проходят;
Windows UI smoke проверяет открытие палитры, синхронизацию, темы, вкладки,
минимальную высоту и границы прокрутки. Реальные ответы NAS и macOS остаются
Unverified. Старые ручные проверки Windows не заменяют повторную проверку
пользователем после рефакторинга.

Итоговый InspectCode (без персональных слоёв IDE) завершился успешно:
ошибок нет, 28 предупреждений и 18 предложений остаются в XML-отчёте
`artifacts/resharper-after.xml`. Это отдельный результат от компиляции без предупреждений.
Замечания не скрыты: захваты disposable-переменных в тестах требуют проверки
порядка освобождения (сессия освобождается раньше handler); изменяемый флаг
progress намеренно отсекает сообщения после завершения запроса. Проверка null
в JSON нужна для повреждённых документов вопреки nullable-аннотациям.
Контрактные поля сохранены для диагностики и дальнейшего отображения;
предложения по шаблонам, async event handlers и области видимости не применялись
автоматически. Полный отчёт остаётся доступен для последующих изменений.

| Требование | Статус |
| --- | --- |
| Пикер, синхронизация HEX и предпросмотра | Implemented / Automated-tested |
| Разделение классов и правила ReSharper | Implemented / Automated-tested |
| Windows desktop после рефакторинга | Automated-tested (smoke); ручная приёмка Unverified |
| Подключение к реальному NAS после рефакторинга | Unverified |
| Проверка macOS | Unverified, отложена пользователем |
