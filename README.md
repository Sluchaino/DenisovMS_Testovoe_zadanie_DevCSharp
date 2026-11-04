# ДенисовМ.С._Тестовое задание_DevC#

Vulnerability Tracker API — REST API на C# / ASP.NET Core (.NET 10).
Автор: Денисов М.С.

Репозиторий: https://github.com/Sluchaino/DenisovMS_Testovoe_zadanie_DevCSharp

Сервис хранит уязвимости в памяти, выполняет фоновые задачи с очередью, настраиваемым лимитом параллельности и отменой. После перезапуска все данные исчезают.

## Допущение о сканировании

В условии не определены инструмент анализа и правила обнаружения. Реализована **имитация**: 10 отменяемых шагов по 1 секунде, затем один заранее заданный демонстрационный результат. Репозиторий не скачивается, код не анализируется. Результат содержит `isSimulation: true`. Демонстрационные находки не добавляются автоматически в `/vulnerabilities`.

Для реального анализа реализуйте `IVulnerabilityScanner` и замените регистрацию `SimulatedVulnerabilityScanner` в `Program.cs`. API и очередь сохраняются. Реальный сканер обязан соблюдать CancellationToken и ожидать остановки всех своих операций перед завершением. Случайных ошибок в имитации нет; ошибки проверяются тестовым сканером.

## Запуск

Требуется .NET SDK 10.0.x. Внешние службы и база данных не нужны. Из корня репозитория:

```sh
dotnet restore VulnerabilityTracker.slnx
dotnet build VulnerabilityTracker.slnx --configuration Release
dotnet run --project src/VulnerabilityTracker.Api
```

- API: http://localhost:5080
- Swagger UI: http://localhost:5080/swagger
- OpenAPI: http://localhost:5080/openapi/v1.json

Остановка: Ctrl+C. Приём новых задач прекращается, активные и ожидающие задачи отменяются. При `--no-launch-profile` адрес задавайте через `--urls` или `ASPNETCORE_URLS`. Swagger доступен во всех окружениях для проверки задания.

## Эндпоинты

| Метод и маршрут | Результат |
| --- | --- |
| `POST /vulnerabilities` | `201`, полная запись с ID и датой, заголовок Location |
| `GET /vulnerabilities/{id}` | `200`, полная запись; `404` для неизвестного ID |
| `POST /scans` | `202`, ID и исходное состояние задачи, Location |
| `GET /scans/{id}` | `200`, состояние, даты, результат/ошибка; `404` для неизвестного ID |
| `POST /scans/{id}/cancel` | `200`, ID, время запроса отмены, success и статус; `404` для неизвестного ID |

ID — GUID. Даты — UTC, ISO 8601. Критичность: `Low`, `Medium`, `High`, `Critical` (строки). Название обязательно, до 200 символов, не только пробелы. URL — абсолютный HTTP(S), до 2048 символов, без встроенных учётных данных. Существование репозитория имитация не проверяет.

Неверные данные: `400 ValidationProblemDetails`. Переполненная/закрытая очередь: `503`, задача не сохраняется. Необработанные ошибки HTTP: `500 ProblemDetails` с traceId без деталей исключения. Ошибка сканера переводит его задачу в `Failed`; очередь продолжает работать.

## Примеры

Bash (в Windows проще использовать Swagger или Invoke-RestMethod):

```sh
curl -i http://localhost:5080/vulnerabilities \
  -H 'Content-Type: application/json' \
  -d '{"name":"SQL injection","severity":"Critical"}'
curl http://localhost:5080/vulnerabilities/ВСТАВЬТЕ_ID
curl -i http://localhost:5080/scans \
  -H 'Content-Type: application/json' \
  -d '{"repositoryUrl":"https://gitlab.com/example/project"}'
curl http://localhost:5080/scans/ВСТАВЬТЕ_ID
curl -X POST http://localhost:5080/scans/ВСТАВЬТЕ_ID/cancel
```

PowerShell:

```powershell
$scan = Invoke-RestMethod http://localhost:5080/scans -Method Post -ContentType 'application/json' -Body '{"repositoryUrl":"https://gitlab.com/example/project"}'
Invoke-RestMethod "http://localhost:5080/scans/$($scan.id)"
Invoke-RestMethod "http://localhost:5080/scans/$($scan.id)/cancel" -Method Post
```

Пример поля result после завершения:

```json
{
  "isSimulation": true,
  "findings": [
    {
      "name": "Demonstration finding: hardcoded secret",
      "severity": "High",
      "file": "demo/Example.cs",
      "line": 12
    }
  ]
}
```

## Очередь и отмена

```text
Queued -> Running -> Completed
                  -> Failed
Queued  -> Cancelled
Running -> Cancelled
```

`ScanService` использует ограниченный `Channel<ScanJob>` и фиксированное число обработчиков. Каждый ждёт завершения своей задачи перед следующей. Брокер не требуется для однопроцессного сервиса с непостоянным состоянием.

Отмена сразу переводит задачу в `Cancelled` и сигнализирует сканеру. Task.Delay прерывается; физическая остановка завершается асинхронно. Место обработчика освобождается только после выхода сканера. Ожидающая отменённая задача остаётся в канале до извлечения обработчиком и до этого занимает место в очереди.

Повторная отмена, отмена Completed/Failed возвращают `success: false` и не меняют дату состояния. При гонке завершения и отмены побеждает первый переход под блокировкой. Терминальное состояние не изменяется. Статус, дата и результат обновляются вместе и возвращаются неизменяемым снимком.

## Настройки

`src/VulnerabilityTracker.Api/appsettings.json`, секция `Scanning`:

| Параметр | По умолчанию | Диапазон | Назначение |
| --- | --- | --- | --- |
| MaxParallelScans | 2 | 1–32 | Число обработчиков |
| QueueCapacity | 100 | 1–10000 | Вместимость ожидающей очереди |
| SimulationSteps | 10 | 1–100 | Число шагов |
| StepDelayMilliseconds | 1000 | 1–60000 | Задержка шага, мс |

Проверяются при запуске; изменения применяются после перезапуска. Переопределение через переменные окружения: например `Scanning__MaxParallelScans=1`. Для ручной проверки очереди установите один обработчик и отправьте несколько сканирований подряд: одно Running, остальные Queued.

## Файлы и логи

- `src/VulnerabilityTracker.Api` — API, модели, хранилище, сканер, очередь, обработка ошибок.
- `tests/VulnerabilityTracker.Tests` — unit-тесты и HTTP integration-тесты через WebApplicationFactory.
- `.github/workflows/ci.yml` — сборка и тесты GitHub Actions на Linux.
- Репозитории не скачиваются. Постоянных файлов состояния/результатов нет.
- Логи выводятся в консоль. HTTP Logging фиксирует метод, путь, статус и длительность. Тела, заголовки и query string не включены в HTTP-лог.
- Ошибки и переходы сканирования логируются с ID задачи. Подробности исключения доступны в консоли, API возвращает общее сообщение.
- Артефакты сборки: bin/ и obj/ проектов, исключены из Git.

Завершённые задачи и уязвимости хранятся до остановки процесса. Очередь ограничена, автоматического удаления истории нет. Сервис рассчитан на один экземпляр приложения.

## Проверка

```sh
dotnet test VulnerabilityTracker.slnx --configuration Release
```

Тесты проверяют создание/чтение, валидацию HTTP, Swagger/OpenAPI, сброс хранилища, лимит параллельности, заполненную очередь, отмену ожидающих/активных задач, повторную отмену, гонку завершения и отмены, ошибку сканера и остановку сервиса. Управляемый тестовый сканер проверяет очередь без десятисекундных задержек.

Документация OpenAPI: https://learn.microsoft.com/aspnet/core/fundamentals/openapi/aspnetcore-openapi?view=aspnetcore-10.0
