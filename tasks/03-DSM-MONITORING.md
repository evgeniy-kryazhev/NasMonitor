# Этап 03 — реальные метрики и высота по содержимому

Разрешён пользователем после приёмки формы, бокового списка NAS и закреплённой кнопки.
macOS по-прежнему отложена. Пользователь дополнительно запросил storage/network/
temperature/uptime со скриншота и автоматическую высоту карточки.

- Один сборщик/сессия на NAS; несколько независимых профилей.
- CPU/RAM/сеть: Utilization v1 get. System v1 info: модель, DSM, температура, uptime.
- Storage.CGI.Storage v1 load_info: логические тома, взвешенный процент заполнения.
- Resource interval 10 s, slow interval 60 s, bounded concurrency, backoff, cancellation.
- Никаких synthetic значений в обычном запуске, отдельные unavailable/denied/stale.
- Выбор источника виджета, OS-secret auto reconnect, ручной OTP, обновление.
- Автовысота общего содержимого в preview и native desktop host; ручная ширина.
- Автотесты mocked HTTP и orchestration; реальный DSM и macOS — manual/Unverified.

Далее требуется пользовательская проверка реального NAS по docs/DSM-MONITORING.md.
До этой проверки не объявлять API совместимым с конкретной моделью/версией DSM.
