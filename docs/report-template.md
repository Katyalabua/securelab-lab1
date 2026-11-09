# Звіт до лабораторної роботи №1

## 1. Ідентифікація стану

- Варіант: 2-A «Трекер інцидентів».
- Гілка: `lab/1-system`.
- Фінальний тег: `v0.1.0`.
- Commit hash: `5fe2e615bac5b0bac83c2baea7c4d8c2ecc4e3b0`.

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

---

---

# Звіт WEB ЛР03

Обраний рівень: «Добре». Рівні накопичувальні; пункти рівня «Відмінно» не входять до обраного рівня.

## Відтворення

- Обраний рівень/оцінка: «Добре».
- Автор/група: Криворучко Катерина, КБ-43.
- Baseline: 2-A «Трекер інцидентів».
- Base tag `v0.2.0`: commit `73f36c4` (на origin, не переміщувався).
- Scaffold: release `lab-03-start-v1`, base_commit `73f36c41509b22e830eff7600e4c65a03f1d6697`; встановлено commit `2d24a5d`.
- Гілка: `lab/3-authentication`, створена від тегу `v0.2.0`.
- Перевірений commit/tag `v0.3.0`: останній коміт коду `1fa1c5a`; тег `v0.3.0` ставиться на наступний коміт зі звітом.
- ОС/SDK: Windows 11; .NET SDK 10.0.302; PostgreSQL 18.4 (Docker, образ `postgres:18.4-alpine3.24`).
- Команди build/tests: `dotnet build SecureLab.sln -c Release`; `dotnet test tests/SecureLab.Api.Tests -c Release`.
- Фактичний результат: збірка без помилок; тести `Passed: 21, Failed: 0, Skipped: 0`.
- Migration/reset: стенд відновлюється скриптом reset із папки `scripts`, виконаним двічі поспіль; обидва запуски завершилися з кодом 0, повторний запуск не створює дублікатів даних.
- Пароль навчальних користувачів задається лише локальною змінною середовища `SeedUsers__Password`; у файлах, Git, логах і доказах його немає.

## Власна реалізація і межі

- **DTO boundary.** Клієнт передає лише `UserName`, `Email`, `DisplayName`, `Password` (реєстрація) та `UserName`, `Password` (вхід). Роль `Reporter` і id призначає сервер. Відповідь реєстрації містить лише `id`, `userName`, `displayName`. Тіло створення інциденту не має поля власника; зайве `ownerUserId` ігнорується. Валідація реєстрації: логін за шаблоном `^[A-Za-z0-9\-._@+]{3,256}$`, email ≤ 256, ім'я ≤ 120, пароль ≤ 128, далі політика Identity (довжина ≥ 10, складність); помилка → `400` з єдиним повідомленням без деталей.
- **Звідки `/api/me` і create беруть id.** З claim `NameIdentifier` у `HttpContext.User`, який сформувала cookie-автентифікація після перевірки пароля. Заголовок `X-Demo-UserId` і поле власника в тілі не використовуються.
- **Cookie options і межа HTTP.** Cookie `.AspNetCore.Identity.Application`: `HttpOnly`, `SameSite=Lax`; `SecurePolicy` — `SameAsRequest` у Development і `Always` поза ним. Редіректи на login і access-denied замінено на `401` і `403`. Межа HTTP: по `http://localhost` у Development прапорця `Secure` немає, тому цей прапорець перевірено на HTTPS-запиті в тесті.
- **Фактичні flags (добрий).** `httponly`, `secure` (HTTPS), `samesite=lax`, `path=/`, без `expires` і `max-age` (сесійна cookie). Перевірено автотестом і в DevTools.
- **Строк дії.** Стандартний, не змінювався: сесійна cookie; термін дії квитка на сервері 14 днів зі ковзним оновленням.
- **Однакові login-відмови.** Невідомий користувач, хибний пароль і порожні поля дають однаковий `401` (статус, content-type, форма тіла). Підтверджено автотестом A-07.
- **Duplicate-контракт (добрий).** Дубль логіна або email → `409` із повідомленням `Identifier already in use`; інші помилки створення → `400`. Регресія: `Register_Duplicate_Returns409`.
- **Rate partition/window/recovery і події (добрий).** Політика `login` на `POST /api/auth/login`: фіксоване вікно, ключ — IP клієнта, за замовчуванням 10 спроб за 60 с (конфігурація `Auth:LoginRateLimit:*`), без черги; перевищення → `429`; після спливу вікна вхід знову доступний. У тесті ліміт 3 за 5 с. Події `Auth event`: тип, час UTC, результат (`success`, `failure`, `rate_limited`), `traceId`.
- **CSRF навчальний борг.** Автентифікація на cookie без antiforgery-токенів; `SameSite=Lax` лише частково знижує ризик; повний захист змінюючих запитів навмисно перенесено на ЛР 6.
- **Обрані поглиблення (відмінний).** Не входить до обраного рівня.

## Докази

Пояснення:

- **Маршрут довіри.** Браузер надсилає cookie → cookie-handler розшифровує й перевіряє її → відновлює `ClaimsPrincipal` → `HttpContext.User` → `RequireAuthorization()` → endpoint читає `NameIdentifier`. Дані від клієнта особою не вважаються.
- **Стандартна перевірка пароля.** `PasswordSignInAsync` порівнює пароль із хешем через стандартний `PasswordHasher` Identity (із сіллю); власної криптографії немає. Блокування акаунта не вмикалося: від перебору входу захищає rate limit.
- **Cookie options і межа HTTP.** Див. розділ «Власна реалізація і межі»: `Secure` залежить від схеми запиту в Development; прапорці перевірено на HTTPS.
- **Чому клієнтський `UserId` не є доказом особи.** Клієнт може підставити довільний id у заголовок чи тіло; особу підтверджує лише cookie, видана після перевірки пароля (тести A-03 і A-05).
- **CSRF-борг.** Див. розділ «Власна реалізація і межі»; закриття — ЛР 6.
- **Endpoint-и перебору акаунтів і компроміси.** _Register_: `409` розкриває, що логін або email зайняті; це прийнятий компроміс для зручності реєстрації; залишковий ризик, бо register не має rate limit. _Login_: канал закрито однаковим `401` (A-07), перебір обмежує rate limit.

| ID                 | Рівень    | Передумова/дія                                | Очікувано                                     | Фактично                                                                                                                                                                                                                                        | Безпечний доказ                                                                                                                                                                                            |
| ------------------ | --------- | --------------------------------------------- | --------------------------------------------- | ----------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------- | ---------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------- |
| T-01               | достатній | register                                      | safe 201                                      | `201`; у тілі лише `id`, `userName`, `displayName`, без password/hash/role                                                                                                                                                                      | автотест `T01_Register_Valid_Returns201_WithoutSensitiveFields`; ручна перевірка в Network (вкладка Response)                                                                                              |
| T-02               | достатній | invalid/duplicate                             | safe rejection                                | слабкий пароль і хибний email → `400`; дубль → `409`; повідомлення без деталей                                                                                                                                                                  | `T02_Register_WeakPassword_Returns400`, `T02_Register_InvalidEmail_Returns400`; ручна перевірка `Register: 409` у браузері                                                                                 |
| T-03               | достатній | модель/схема + Identity verifier              | password не відкрито                          | у БД лише hash пароля (відкритого пароля немає); перевірку виконує Identity                                                                                                                                                                     | вивід `Schema.ps1`: структура таблиці користувачів без значень пароля                                                                                                                                      |
| A-01               | достатній | anonymous protected request                   | 401, no redirect                              | `POST /api/incidents` без cookie → `401`, без `Location` і `WWW-Authenticate`                                                                                                                                                                   | `A01_Anonymous_CreateIncident_Returns401_NotRedirect`; ручний запит з `lab-03-checks.http`                                                                                                                 |
| A-02               | достатній | login → me                                    | principal user                                | login `204`, `/api/me` `200` для `alice`                                                                                                                                                                                                        | `A02_Login_Me_ReturnsCurrentUser`; ручна перевірка в браузері                                                                                                                                              |
| A-05               | достатній | Bob створює запис із forged owner Alice       | новий owner = Bob; зайве поле ігнорується     | `201`, власник у БД Bob                                                                                                                                                                                                                         | `A05_Create_WithForgedOwner_KeepsPrincipalAsOwner`; ручна перевірка з `lab-03-checks.http` (власник у деталях — Bob)                                                                                       |
| S-01               | достатній | аналіз credentials boundary                   | без витоків                                   | пароль лише з env; cookie недоступна з JS (`document.cookie` порожній); події без credentials; у змінах відносно `v0.2.0` секретів немає                                                                                                        | перегляд змін `git diff v0.2.0..HEAD`; перевірка в DevTools                                                                                                                                                |
| A-03/04/06/07      | добрий    | окремий запис на кожний виконаний ID          | контракт методички                            | **A-03:** заголовок `X-Demo-UserId` ігнорується, `/api/me` повертає Alice. **A-04:** create без owner → власник у БД = Bob. **A-06:** logout `204`, далі `/api/me` `401`. **A-07:** однаковий `401` для невідомого користувача й хибного пароля | `A03_ForeignDemoHeader_DoesNotChangeCurrentUser`, `A04_Create_WithoutOwner_UsesPrincipalAsOwner`, `A06_Logout_ThenProtectedRequest_Returns401`, `A07_UnknownUser_And_WrongPassword_GiveSameExternalResult` |
| Browser flow       | добрий    | п'ять перевірок етапу 4                       | state/profile/create/logout                   | реєстрація не логінить (`/api/me` `401`); login `204` і профіль `alice`; create без owner → `201`; logout очищає профіль і `/api/me` `401`; Local/Session Storage порожні                                                                       | сценарій `auth-lab.html`, спостереження в DevTools (Network, Application)                                                                                                                                  |
| Duplicate          | добрий    | обраний контракт і регресія                   | послідовний 400/409                           | дубль логіна і email → `409`, інше → `400`                                                                                                                                                                                                      | `Register_Duplicate_Returns409`; ручна перевірка `Register: 409`                                                                                                                                           |
| Cookie flags       | добрий    | тест фактичних flags                          | контракт фіксованого профілю                  | `httponly`, `secure` (HTTPS), `samesite=lax`, `path=/`, без `expires`/`max-age`                                                                                                                                                                 | `Login_OverHttps_SetsExpectedCookieFlags`; `Set-Cookie` у DevTools (значення cookie не фіксується)                                                                                                         |
| Rate limit і події | добрий    | перевищення ліміту, відновлення, аналіз події | 429, відновлення після вікна, без credentials | при ліміті 3: `401, 401, 401, 429`; після вікна знову `401`; у логу `Auth event … failure/rate_limited` без логіна і пароля                                                                                                                     | `Login_ExceedingLimit_Returns429_ThenRecoversAfterWindow`; ручна перевірка: статуси в Network і рядки `Auth event` у консолі API                                                                           |
| Поглиблення        | відмінний | —                                             | —                                             | не входить до обраного рівня                                                                                                                                                                                                                    | —                                                                                                                                                                                                          |

**Обґрунтування partition key та виключених полів події.** Ключ партиції — IP клієнта (`unknown`, якщо адреси немає, як у тестах). Ключ за іменем користувача дозволив би зловмиснику блокувати чужий акаунт хибними спробами; ключ за IP обмежує джерело перебору. Недолік: клієнти за спільним NAT ділять ліміт (прийнятно для навчальної роботи). Подія містить тип, час UTC, результат і `traceId`. Виключено ім'я користувача (у полі логіна може опинитися пароль, а лог розкривав би існування акаунтів), пароль, cookie, hash та IP (персональні дані; для кореляції достатньо `traceId`).

Threat analysis (рівень «Відмінно»): не входить до обраного рівня.

## Декларація використання ШІ

ШІ використано (Claude, Anthropic) для пояснень, планування етапів і чернеток коду. Код і команди адаптовано автором під власний проєкт та перевірено збіркою, автотестами й ручними перевірками в браузері.

| Дата       | Сервіс/модель, якщо відома | Завдання або короткий запит без секретів     | Використана частина відповіді   | Авторська адаптація                                         | Перевірка і фактичний результат               |
| ---------- | -------------------------- | -------------------------------------------- | ------------------------------- | ----------------------------------------------------------- | --------------------------------------------- |
| 2026-11-08 | Claude                     | План виконання ЛР 3, налаштування scaffold   | порядок етапів і дій з Git      | власні шляхи, гілка від `v0.2.0`                            | збірка без помилок, історія коммітів          |
| 2026-11-08 | Claude                     | Автентифікація, захист створення, rate limit | схеми коду і тестів             | власні межі валідації, виправлення розташування налаштувань | автотести зелені, ручні перевірки 401/200/429 |
| 2026-11-09 | Claude                     | Браузерний сценарій і звіт                   | каркас HTML/JS, структура звіту | `textContent`, очищення пароля, власні формулювання         | перевірка в DevTools                          |
