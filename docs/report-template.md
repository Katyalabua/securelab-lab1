# Звіт до лабораторної роботи №1

## 1. Ідентифікація стану

- Варіант: 2-A «Трекер інцидентів».
- Гілка: `lab/1-system`.
- Фінальний тег: `v0.1.0`.
- Commit hash: `TODO`.

## 2. Змінений маршрут

Реалізовано точку розширення `GET /api/incidents/severity-summary`, яка до змін
повертала `501 Not Implemented`.

```text
клік на кнопку "Показати підсумок" у Client/app.js (loadSeveritySummary)
  → GET /api/incidents/severity-summary
  → IncidentEndpoints.GetSeveritySummaryAsync
  → IncidentQueries.GetSeveritySummaryAsync
  → SecureLabDbContext.Incidents
  → PostgreSQL: таблиця incidents
  → IncidentSeveritySummaryResponse
  → JSON response
  → renderSeveritySummary: textContent у Client/app.js
```

Ключові файли:

- `src/SecureLab.Api/Presentation/Endpoints/IncidentEndpoints.cs`
- `src/SecureLab.Api/Application/Incidents/IncidentQueries.cs`
- `src/SecureLab.Api/Presentation/Contracts/IncidentResponses.cs`
- `src/SecureLab.Api/Client/index.html`
- `src/SecureLab.Api/Client/app.js`

Додатково досліджено (без змін коду) вже готовий маршрут `GET /api/incidents/{id}`
через DevTools Network, код `IncidentEndpoints.GetDetailsAsync` →
`IncidentQueries.GetDetailsAsync` → `SecureLabDbContext.Incidents`, і SQL-звірку
безпосередньо в PostgreSQL.

## 3. Виконані зміни

1. Додано DTO `IncidentSeveritySummaryResponse(string Severity, int Count)` у
   `Presentation/Contracts/IncidentResponses.cs`.
2. Додано метод `IncidentQueries.GetSeveritySummaryAsync`, що виконує
   `AsNoTracking().GroupBy(i => i.Severity)` і повертає лише наявні групи
   (без доповнення відсутніх рівнів нулями), відсортовані лексикографічно.
3. У `IncidentEndpoints.cs` замінено заглушку 501 на реєстрацію
   `group.MapGet("/severity-summary", GetSeveritySummaryAsync)` з новим
   приватним методом-обробником, що повертає `Results.Ok(...)`.
4. У `Client/index.html` додано секцію з кнопкою `#summary-button` і
   списком `#summary-list`.
5. У `Client/app.js` додано функції `renderSeveritySummary` і
   `loadSeveritySummary`, підключені через `apiFetch`, і обробник кліку
   на кнопку. Вивід результату — через `textContent`, без `innerHTML`.

## 4. Перевірка

## 4. Перевірка

| ID   | Передумови                                     | Дія                                                     | Очікувано                                                                                         | Фактично                                                                                                     | Доказ                                                                 |
| ---- | ---------------------------------------------- | ------------------------------------------------------- | ------------------------------------------------------------------------------------------------- | ------------------------------------------------------------------------------------------------------------ | --------------------------------------------------------------------- |
| T-01 | PostgreSQL healthy, API запущено               | GET /health                                             | 200                                                                                               | 200                                                                                                          | скріншот/curl-вивід /health                                           |
| T-02 | відновлений seed                               | GET /api/incidents?status=Triaged                       | 200, список відповідає фільтру                                                                    | 200, отримано інцидент(и) зі статусом Triaged                                                                | Network-запис / .http response                                        |
| T-03 | відновлений seed                               | GET /api/incidents?status=Resolved                      | 200 з порожнім масивом                                                                            | 200, `[]`                                                                                                    | .http response                                                        |
| T-04 | відновлений seed                               | GET /api/incidents/99999999-9999-9999-9999-999999999999 | 404 Problem Details                                                                               | 404, Content-Type application/problem+json                                                                   | Network-запис, traceId зафіксовано                                    |
| T-05 | відновлений seed                               | GET /api/incidents?status=Unknown                       | 400 Validation Problem Details                                                                    | 400                                                                                                          | .http response                                                        |
| T-06 | реалізований етап 3, відновлений seed          | GET /api/incidents/severity-summary                     | 200; Low, Medium, High по одному елементу з count: 1, без Critical (політика "лише наявні групи") | 200, отримано `[{"severity":"High","count":1},{"severity":"Low","count":1},{"severity":"Medium","count":1}]` | .http response до і після (501 → 200)                                 |
| T-07 | реалізовано етап 3, API і клієнт запущено      | натиснути кнопку "Показати підсумок"                    | UI безпечно показує успішний результат                                                            | список High:1, Low:1, Medium:1 відображено на сторінці                                                       | скріншот UI + Network-запис GET /api/incidents/severity-summary (200) |
| T-08 | після контрольованих експериментів із даними   | --reset-database, повторити T-02 і T-06                 | seed повертає стенд до відомого стану                                                             | після reset список і підсумок відповідають початковому seed                                                  | вивід команди reset + повторний response                              |
| T-09 | реалізовано severity-summary, відновлений seed | GET /api/incidents/severity-summary                     | 200; Low, Medium, High по одному елементу, без Critical (політика "лише наявні групи")            | 200, отримано список без Critical                                                                            | .http response                                                        |
| T-10 | реалізовано фільтр status, відновлений seed    | GET /api/incidents/severity-summary?status=Triaged      | 200 з непорожнім результатом                                                                      | 200, непорожній список                                                                                       | .http response                                                        |
| T-11 | реалізовано фільтр status, відновлений seed    | GET /api/incidents/severity-summary?status=Closed       | 200 з порожнім масивом (немає інцидентів зі статусом Closed)                                      | 200, `[]`                                                                                                    | .http response                                                        |
| T-12 | реалізовано фільтр status                      | GET /api/incidents/severity-summary?status=Unknown      | 400 Validation Problem Details                                                                    | 400, помилка про допустимі значення status                                                                   | .http response                                                        |

## 5. Security-сценарій

Для робіт із навмисно вразливим станом зафіксуйте: гіпотезу, стан «до»,
мінімальний локальний PoC, спостереження, першопричину, виправлення, повторну
перевірку, позитивну регресію та залишковий ризик.

Не застосовується до ЛР 1: робота не містить навмисно вразливого стану,
реалізується лише точка розширення (severity-summary) без зміни схеми БД
чи введення дефекту безпеки.

## 6. Висновок

Стенд (PostgreSQL, ASP.NET Core API, browser client) успішно розгорнуто й
перевірено, включно з відновленням через seed/reset. Досліджено наскрізний
маршрут GET /api/incidents/{id} від кліку в браузері до таблиці PostgreSQL і
назад, зафіксовано щонайменше три межі довіри. Реалізовано точку розширення
GET /api/incidents/severity-summary: обрано політику "лише наявні групи" з
лексикографічним порядком, результат повертається окремим DTO без зайвих
полів entity, browser client викликає endpoint і безпечно показує результат
через textContent. Усі базові HTTP-сценарії (200/400/404) та автоматизовані
тести пройдені успішно після останньої зміни коду. Додано необов'язковий query parameter `status` до `GetSeveritySummaryAsync`, з
перевіркою за allowlist (`Enum.TryParse`/`Enum.IsDefined`) і поверненням 400
Validation Problem Details для некоректного значення. У клієнті додано стан
"Завантаження…" перед запитом, окрім уже наявних станів порожнього результату
та безпечної помилки.
