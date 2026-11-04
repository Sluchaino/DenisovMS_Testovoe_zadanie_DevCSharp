# ДенисовМ.С._Тестовое задание_DevC#

Vulnerability Tracker API — REST API на C# / ASP.NET Core (.NET 10).
Автор: Денисов М.С.
Репозиторий: https://github.com/Sluchaino/DenisovMS_Testovoe_zadanie_DevCSharp

Сервис хранит уязвимости в памяти и выполняет реальный статический анализ C# из публичных Git-репозиториев. Есть очередь, настраиваемый лимит параллельности и отмена. После перезапуска все записи исчезают.

## Запуск

Требуются .NET SDK 10.0.x и Git в PATH. Docker, база данных и внешние сканеры не нужны.

```sh
git clone https://github.com/Sluchaino/DenisovMS_Testovoe_zadanie_DevCSharp.git
cd DenisovMS_Testovoe_zadanie_DevCSharp
dotnet restore VulnerabilityTracker.slnx
dotnet run --project src/VulnerabilityTracker.Api
```

- API: http://localhost:5080
- Swagger UI: http://localhost:5080/swagger
- OpenAPI: http://localhost:5080/openapi/v1.json

При запуске с `--no-launch-profile` задайте адрес через `--urls` или `ASPNETCORE_URLS`. Swagger доступен во всех окружениях для проверки задания. Остановка: Ctrl+C; новые задачи не принимаются, активные и ожидающие отменяются.

## Что делает сканер

1. Принимает HTTPS URL публичного репозитория на github.com, gitlab.com или bitbucket.org. Учётные данные, query string, fragment и нестандартные порты запрещены. Перенаправления Git отключены; для переименованного репозитория передавайте актуальный URL.
2. Клонирует последний коммит ветки по умолчанию (`--bare --depth=1 --single-branch`). Приватные репозитории и авторизация пока не поддерживаются.
3. Читает дерево и обычные `.cs` файлы непосредственно из Git-объектов. Рабочая копия не разворачивается, код не собирается и не запускается. Подмодули и LFS-объекты не скачиваются. Символические ссылки не разыменовываются. Настройки Git пользователя/системы, credential helpers и hooks не используются.
4. Разбирает C# через Roslyn и применяет пять синтаксических правил. Комментарии и текст, содержащий код внутри строк, не считаются исполняемыми конструкциями.
5. Возвращает находки с ID правила, критичностью, путём и номером строки (с 1), SHA коммита и счётчиками файлов. `isSimulation` равен `false`. Значения найденных секретов и исходные строки в результат не включаются.
6. Удаляет временную копию после успеха, ошибки или отмены. При штатной отмене останавливает Git с дочерними процессами и дожидается завершения операции до освобождения обработчика.

| Правило | Критичность | Проверяемая конструкция |
| --- | --- | --- |
| VT001 | High | Непустой строковый литерал в переменной, свойстве с инициализатором или присваивании с именем password/passwd/apiKey/accessToken/clientSecret/privateKey/connectionString |
| VT002 | High | Callback проверки сертификата, безусловно возвращающий true, или DangerousAcceptAnyServerCertificateValidator |
| VT003 | Medium | MD5/SHA1 через Create/HashData или известные конструкторы реализаций |
| VT004 | High | Интерполяция или динамическая конкатенация непосредственно в SQL-конструкторе, CommandText, FromSqlRaw/ExecuteSqlRaw/ExecuteSqlRawAsync |
| VT005 | Medium | DES, TripleDES, RC2 через Create или известные конструкторы реализаций |

Это ограниченный **синтаксический анализ**, а не полноценный аудит безопасности. Нет семантической модели, анализа потоков данных или базы CVE зависимостей. Имена API сопоставляются по синтаксису: одноимённые пользовательские методы могут дать ложные срабатывания. MD5 может применяться для некриптографической контрольной суммы, а встроенная строка — быть тестовыми данными. SQL, предварительно сохранённый в отдельную переменную, через эти правила не отслеживается. Алиасы типов и косвенные callback могут быть пропущены. Неактивные ветви `#if` не анализируются; проектные define не загружаются. Исходник не обязан компилироваться. Каждая находка требует проверки человеком, отсутствие находок не доказывает безопасность.

Сканируются только `.cs`; другие языки, конфигурационные файлы и зависимости не анализируются. Пропускаются bin/obj/packages/vendor/node_modules, `.g.cs`, `.generated.cs`, `.Designer.cs`, слишком большие, бинарные и символьные `.cs` файлы. Их количество возвращается как `skippedFiles` (только кандидаты `.cs`). Git LFS pointer не является содержимым исходника. При превышении общего числа/объёма исходников или числа находок сканирование завершается ошибкой вместо молчаливого усечения результата. Пустой/не C# репозиторий может завершиться с `scannedFiles: 0`.

## Эндпоинты

| Метод и маршрут | Результат |
| --- | --- |
| POST /vulnerabilities | 201, запись с ID и датой, Location |
| GET /vulnerabilities/{id} | 200, полная запись; 404 для неизвестного ID |
| POST /scans | 202, исходное состояние и ID задачи, Location |
| GET /scans/{id} | 200, состояние, даты и результат/ошибка; 404 для неизвестного ID |
| POST /scans/{id}/cancel | 200, ID, время запроса, success и статус; 404 для неизвестного ID |

ID — GUID, даты — UTC/ISO 8601. Критичность: Low, Medium, High, Critical (строки). Название обязательно, до 200 символов, не только пробелы. URL — до 2048 символов.

Неверные данные: 400 ValidationProblemDetails. Полная/закрытая очередь: 503 без сохранения задачи. Необработанная HTTP-ошибка: 500 ProblemDetails с traceId. Ошибки Git/анализа и таймаут переводят задачу в Failed; другие задачи продолжают выполняться. Подробности ошибок сервера доступны в консоли с ID задачи; API возвращает общее сообщение. Находки автоматически не добавляются в /vulnerabilities.

## Пример проверки

В `samples/VulnerableExample.cs` находятся пять специально подготовленных подозрительных конструкций с фиктивным секретом. Файл не входит в проект приложения и не собирается. Сканирование этого репозитория должно показать находки VT001–VT005 в samples/VulnerableExample.cs.

PowerShell:

```powershell
$scan = Invoke-RestMethod http://localhost:5080/scans -Method Post -ContentType 'application/json' -Body '{"repositoryUrl":"https://github.com/Sluchaino/DenisovMS_Testovoe_zadanie_DevCSharp"}'
Invoke-RestMethod "http://localhost:5080/scans/$($scan.id)"
# При необходимости:
Invoke-RestMethod "http://localhost:5080/scans/$($scan.id)/cancel" -Method Post
```

Bash:

```sh
curl -i http://localhost:5080/vulnerabilities \
  -H 'Content-Type: application/json' \
  -d '{"name":"SQL injection","severity":"Critical"}'
curl -i http://localhost:5080/scans \
  -H 'Content-Type: application/json' \
  -d '{"repositoryUrl":"https://github.com/Sluchaino/DenisovMS_Testovoe_zadanie_DevCSharp"}'
curl http://localhost:5080/scans/ВСТАВЬТЕ_ID
curl -X POST http://localhost:5080/scans/ВСТАВЬТЕ_ID/cancel
```

Пример поля result (сокращённый список находок):

```json
{
  "isSimulation": false,
  "findings": [
    {
      "name": "Potential hardcoded credential",
      "severity": "High",
      "file": "samples/VulnerableExample.cs",
      "line": 9,
      "ruleId": "VT001"
    }
  ],
  "scannedFiles": 20,
  "skippedFiles": 0,
  "commit": "SHA проанализированного коммита"
}
```

## Очередь и отмена

```text
Queued -> Running -> Completed
                  -> Failed
Queued  -> Cancelled
Running -> Cancelled
```

Ограниченный Channel и фиксированное число обработчиков обеспечивают лимит параллельности. Отмена сразу меняет статус на Cancelled, передаёт CancellationToken загрузке/анализу и останавливает Git. Обработчик освобождается после выхода сканера и очистки копии. Отменённая ожидающая задача занимает место в канале до получения обработчиком, который её пропускает.

Повторная отмена, отмена Completed/Failed возвращают success: false, не меняя дату состояния. В гонке завершения и отмены побеждает первый переход под блокировкой. Терминальный статус не перезаписывается; снимок статуса, даты и результата согласован.

## Настройки

Файл: src/VulnerabilityTracker.Api/appsettings.json. Проверяются при запуске, изменения применяются после перезапуска. Переменные окружения: например `Scanning__MaxParallelScans=1` или `RepositoryScanning__TimeoutSeconds=60`.

| Секция и параметр | По умолчанию | Назначение |
| --- | --- | --- |
| Scanning:MaxParallelScans | 2 | Одновременные задачи, 1–32 |
| Scanning:QueueCapacity | 100 | Вместимость очереди, 1–10000 |
| RepositoryScanning:GitExecutable | git | Имя/абсолютный путь Git |
| RepositoryScanning:TimeoutSeconds | 120 | Общий таймаут загрузки и анализа, 1–600 с |
| RepositoryScanning:MaxFiles | 500 | Максимальное число анализируемых C# файлов |
| RepositoryScanning:MaxFileBytes | 1000000 | Порог пропуска отдельного файла |
| RepositoryScanning:MaxSourceBytes | 20000000 | Общий лимит исходников |
| RepositoryScanning:MaxRepositoryBytes | 100000000 | Лимит размера временной Git-копии |
| RepositoryScanning:MaxFindings | 1000 | Максимальное число находок |

Объём Git-копии проверяется примерно каждые 100 мс при загрузке и после неё; это ограничение приложения, возможен кратковременный выход за порог между проверками. Вывод Git также ограничен; список дерева ограничен 4 млн символов. Копии находятся в `<системный temp>/vulnerability-tracker/<случайный GUID>/`. Если процесс аварийно завершён до finally, каталог может остаться; путь ошибки очистки выводится в лог. При обычном завершении он удаляется. Git должен быть доступен учётной записи сервиса, а исходящий HTTPS к выбранному провайдеру — разрешён.

## Файлы и логи

- src/VulnerabilityTracker.Api/Scanning — Git, анализатор и реальный сканер.
- src/VulnerabilityTracker.Api — API, модели, память, очередь и ошибки.
- tests/VulnerabilityTracker.Tests — тесты правил, Git-объектов, лимитов, отмены и API. Имитация осталась только как тестовая подмена; приложение её не использует.
- samples — пример для ручной проверки.
- .github/workflows/ci.yml — сборка и тесты на Linux.
- Логи — консоль: HTTP метод, путь, статус и длительность, переходы и ошибки задач с ID. Тела, заголовки и query string не включены в HTTP Logging.
- bin/ и obj/ — артефакты сборки, исключены из Git.

История задач/уязвимостей хранится в памяти до остановки, автоматического удаления истории нет. Сервис рассчитан на один экземпляр. Постоянных файлов результатов нет.

## Тесты

```sh
dotnet test VulnerabilityTracker.slnx --configuration Release
```

Автотесты не требуют сети: анализируют подготовленный C# и локальный Git-репозиторий, а HTTP-тесты используют управляемый сканер. Проверяются правила и безопасные варианты, URL, чтение объектов без checkout, очистка после ошибки/отмены, лимиты, таймаут, очередь, отмена и её гонка с завершением, обработка ошибок и API.

Использованные API: [Roslyn syntax analysis](https://learn.microsoft.com/dotnet/csharp/roslyn-sdk/get-started/syntax-analysis), [Git clone](https://git-scm.com/docs/git-clone), [ASP.NET OpenAPI](https://learn.microsoft.com/aspnet/core/fundamentals/openapi/aspnetcore-openapi?view=aspnetcore-10.0).
