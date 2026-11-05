# Звіт WEB ЛР 2

## 1. Ідентифікація

- ПІБ і група: Криворучко Катерина Сергіївна, КБ-43.
- Спільний baseline 2-A «Трекер інцидентів»; індивідуального номера немає.
- Заявлений рівень: добрий («Добре»).
- Гілка: `lab/2-input-sqli` (створена від тегу `v0.1.0`).
- Квитанція `.scaffolds/lab-02.json`: release `lab-02-start-v1`; base_commit `9b82a5f5d653f6806b0e08e22180f83bd9e9e055`; entry_parent `9b82a5f5d653f6806b0e08e22180f83bd9e9e055`.
- Vulnerable commit: `b2f04c692a79a810affdb20251966009f2c9744a`.
- Fixed commit коду: `871e4974a3cae5c6d0f40111f2f1ecd006dc97a4`. Тег `v0.2.0` стоїть на commit із завершеним звітом.
- DEL-01: <URL приватного remote або ім'я bundle `securelab-lr02.bundle`>.

## 2. Контракт і CP-01

`POST /api/incidents` приймає окремий request DTO `CreateIncidentRequest` із полями `title`, `description`, `severity`, `occurredAtUtc`. Поля `id`, `ownerUserId`, `status`, `createdAtUtc`, `updatedAtUtc` у DTO відсутні й встановлюються лише сервером (власник — Аліса Коваль з навчального контексту, статус `New`). Відповідь формує окремий `CreatedIncidentResponse`; entity напряму не повертається.

Правила на сервері (помилки збираються в один `Dictionary<string, string[]>` і повертаються через `Results.ValidationProblem` до звернення до БД):

- `title`: не порожній після `Trim()`, довжина вхідного значення ≤ 160; ключ помилки `title`.
- `description`: не порожній після `Trim()`, довжина ≤ 4000; ключ `description`.
- `severity`: `Enum.TryParse` без урахування регістру та `Enum.IsDefined` (Low, Medium, High, Critical); ключ `severity`.
- `occurredAtUtc`: обов'язкове, не пізніше `now + 5 хвилин`, перед записом `ToUniversalTime()`; ключ `occurredAtUtc`.
- Cross-field (T-09): для `High`/`Critical` опис після `Trim()` має бути ≥ 40 символів; `400`, ключ `description`.
- Предметний конфлікт (T-03): однаковий `title` після `Trim()` з урахуванням регістру в статусі New, Triaged, InProgress або Resolved дає `409`; Closed не блокує.

**CP-01.** Одного `Enum.TryParse` недостатньо: рядок `"7"` успішно перетворюється на `(IncidentSeverity)7`, хоча такого елемента в enum немає, тому додано `Enum.IsDefined`; запит із `severity: "7"` повертає `400` з ключем `severity` і повідомленням «Допустимі значення: Low, Medium, High, Critical.». Елемент `<select>` у браузері не контролює API, бо запит можна надіслати будь-яким HTTP-клієнтом, тому перевірка має бути на сервері. Маршрут: JSON → request DTO → server-side validation → `400 Problem Details`. Поля `ownerUserId` і `status` не потрапляють у модель, бо їх немає в request DTO, а зайві поля JSON ігноруються: запит із `id` `11111111-1111-1111-1111-111111111111`, `ownerUserId` `22222222-2222-2222-2222-222222222222` і `status: "Closed"` створив запис із новим `id` `b7a2265d-dc20-407e-81c8-c12f0e81fd3c`, статусом `New` і власником Аліса Коваль (розділ 8, п. 3).

## 3. Security-сценарій і CP-02/CP-03

1. **Контекст і гіпотеза.** Локальний endpoint `GET /api/incidents/search` приймає недовірені `q` і `sortBy`. Гіпотеза: значення `q` потрапляє в SQL-текст, тому дані можуть змінити структуру запиту.
2. **Стан до.** Vulnerable commit `b2f04c692a79a810affdb20251966009f2c9744a`; файл `src/SecureLab.Api/Scaffolding/Lab02Endpoints.cs`, обробник `GET /api/incidents/search` (метод `MapLab02Endpoints`). SQL збирається конкатенацією в рядках 21–22 і виконується через `FromSqlRaw` у рядку 23; гілка `_ => sortBy` (рядок 19) передає довільний `sortBy` в `ORDER BY`.
3. **Мінімальний PoC.** Лише локальний read-only сценарій C із `tests/http/lab-02-checks.http`, на локальній БД зі штучним seed. Деструктивних дій, stacked statements і запитів до сторонніх систем не виконувалось. Рядок payload тут не публікується.
4. **Спостереження.** A (`q=USB`): `200`, один запис. B (значення, якого немає): `200`, `[]`. C: `200` і **5 записів** (усі записи таблиці), хоча значення нічого не збігалося. D (`O'Brien`) і E (`комп'ютерного`): `500`. У журналі API для D і E було `Parameters=[]`, а введений текст стояв усередині SQL; для D журнал містив `Failed executing DbCommand` і `42601: syntax error at or near "Brien"`. Апостроф закрив рядковий літерал, `OR TRUE` став умовою, `--` закоментував решту запиту.
5. **Першопричина.** `q` (і довільний `sortBy`) з'єднувались із SQL-текстом до виконання і передавались у `FromSqlRaw` готовим текстом, тому СУБД не могла відрізнити дані від структури запиту.
6. **Виправлення.** Пошук переписано на LINQ з `EF.Functions.ILike` і параметром `pattern`, у якому екрановано `\`, `_`, `%`. `sortBy` обробляється через `switch` з allowlist (`createdAtUtc`, `severity`, `status`), невідоме значення повертає `400` із ключем `sortBy`. Серверну валідацію контракту створення збережено.
7. **Retest і позитивна регресія.** Той самий сценарій C повертає `200` і `[]`. Звичайний пошук повертає той самий запис, `O'Brien` і `комп'ютерного` повертають `200` та відповідні записи, `%` шукається буквально (`[]`). У журналі непорожній список параметрів (`@pattern`, `@pattern0`, `@p`), у SQL — `ILIKE @pattern ESCAPE '\'`.
8. **Залишковий ризик.** Перевірка не змінює авторизацію (ЛР 3), не обмежує ресурси чи пагінацію, не охоплює інші endpoint-и та політику логування production.

**CP-02.** Фільтр апострофа чи слів на кшталт `OR` не усуває механізм уразливості: легітимні дані (наприклад, `O'Brien`) ламаються, а дані й структура запиту лишаються змішаними. Виправлення — передача значення окремо від тексту запиту (параметр) і allowlist для структури (`sortBy`).

**CP-03.** Раніше СУБД отримувала один текст запиту після конкатенації (у журналі `Parameters=[]`), тепер значення `pattern` передається окремим параметром, а SQL-структуру вибирає серверний allowlist. Різницю між vulnerable і fixed commit показує `git diff b2f04c6..871e497` (фрагмент у розділі 8, п. 9).

## 4. Фактичні перевірки DEL-02

| ID   | Сценарій                 | Передумови                                | Дія                                                           | Очікувано                                   | Фактично                                                                                                | Доказ              |
| ---- | ------------------------ | ----------------------------------------- | ------------------------------------------------------------- | ------------------------------------------- | ------------------------------------------------------------------------------------------------------- | ------------------ |
| T-01 | Коректне створення       | reset, унікальний title                   | `POST /api/incidents`                                         | `201`, response DTO без зайвих полів        | `201`, `Location`, id `86939580-82f2-450f-9820-177ff1c6565e`, title, severity `Low`, status `New`, часи | розділ 8, п. 1     |
| T-02 | Некоректний DTO          | reset                                     | `POST` з severity `"7"`, порожнім title, без дати, датою 2099 | `400`, `application/problem+json`           | чотири запити → `400`, ключі `severity`, `title`, `occurredAtUtc`, `occurredAtUtc`; автотест проходить  | розділ 8, п. 2, 10 |
| T-03 | Предметний конфлікт      | існує активний інцидент з тим самим title | повторний `POST`                                              | `409` у Problem Details                     | `409`, `application/problem+json`, title «Конфлікт»; автотест проходить                                 | розділ 8, п. 4, 10 |
| S-01 | SQLi до виправлення      | vulnerable commit, local seed             | сценарій C                                                    | небажано розширена вибірка                  | C: `200` і 5 записів; D і E: `500`; `Parameters=[]`                                                     | розділ 8, п. 6     |
| S-02 | Retest SQLi              | fixed commit, той самий seed              | той самий сценарій C                                          | порожній результат                          | `200`, `[]`; у журналі `@pattern`, `@pattern0`                                                          | розділ 8, п. 7     |
| T-04 | Позитивна регресія       | fixed commit                              | звичайний пошук і пошук з апострофом                          | коректні записи, без `500`                  | `USB` — один запис; `O'Brien`, `комп'ютерного` — `200` і записи; `%` — `[]`                             | розділ 8, п. 7     |
| T-05 | Невідоме сортування      | fixed commit                              | `GET …/search?sortBy=price`                                   | `400`                                       | `400`, `errors.sortBy` з переліком; stack trace і SQL відсутні                                          | розділ 8, п. 8     |
| T-06 | Ресурс не знайдено       | відсутній, синтаксично коректний id       | `GET /api/incidents/99999999-9999-9999-9999-999999999999`     | `404` у Problem Details                     | `404`, `application/problem+json`, title «Інцидент не знайдено»                                         | розділ 8, п. 5     |
| T-09 | Cross-field 2-A          | унікальні title                           | два `POST` із `High` і description 39 та 40 символів          | перший `400`, другий проходить              | 39 символів → `400`, ключ `description`; 40 символів → `201`, id `ec6817c2-c106-4c25-aedd-7327df0ba3e8` | розділ 8, п. 3     |
| A-01 | Огляд data-access points | fixed commit                              | `git grep`, `data-access-scan.py`                             | для кожного збігу файл, категорія, висновок | збіг один: `DatabaseBootstrap.cs:29`                                                                    | розділ 8, п. 9     |

**Таблиця A-01.**

| Файл:метод / рядок                                        | Категорія                                                         | Висновок                                                                              |
| --------------------------------------------------------- | ----------------------------------------------------------------- | ------------------------------------------------------------------------------------- |
| `DatabaseBootstrap.cs:29`, reset (`ExecuteSqlRawAsync`)   | статичний службовий SQL (`TRUNCATE`)                              | безпечно: рядок константний, назви таблиць фіксовані моделлю, даних користувача немає |
| `Lab02Endpoints.cs`, обробник `GET /api/incidents/search` | LINQ, значення через параметр `pattern`; `sortBy` через allowlist | безпечно; на vulnerable commit були конкатенація і `FromSqlRaw`, виправлено           |
| `FromSqlRaw`, `ORDER BY` на fixed commit                  | збігів немає                                                      | результат огляду зафіксовано                                                          |

Build і baseline-тести після останньої зміни коду: `dotnet build` і `dotnet test` пройшли успішно, у запуску Total: 2, Passed: 2, Failed: 0 (розділ 8, п. 10).

## 5. Обмеження і висновок

- Перевірка `AnyAsync` перед записом навчає предметного конфлікту, але не гарантує унікальність за одночасних запитів; у реальному застосунку потрібен унікальний індекс або транзакційне рішення. Міграцію чи новий індекс у ЛР 2 не додавали за умовою.
- Власник нового інциденту — фіксований навчальний користувач Аліса Коваль; справжню автентифікацію буде додано в ЛР 3.
- Зіпсований JSON у стартовому конвеєрі дає `500` (`BadHttpRequestException`) до виклику обробника; це окремий дефект, не доказ валідації.
- Перевірено лише локально на штучних даних; інші endpoint-и, авторизацію, пагінацію й production-логування не перевіряли.

Висновок: базові правила контракту 2-A, cross-field правило та конфлікт реалізовано й перевірено; SQLi локалізовано в рядках 19 і 21–23 vulnerable commit і усунено параметризацією (`EF.Functions.ILike`) та allowlist для `sortBy`; retest і позитивна регресія пройшли.

## 6. Декларація використання ШІ

| Сервіс             | Задачі                                                                                             | Авторська адаптація/інтерпретація                                                                                                               | Фактична перевірка                                                                                                |
| ------------------ | -------------------------------------------------------------------------------------------------- | ----------------------------------------------------------------------------------------------------------------------------------------------- | ----------------------------------------------------------------------------------------------------------------- |
| Claude (Anthropic) | пояснення методички, план виконання, чернетки коду валідації, пошуку й тестів, шаблон тексту звіту | адаптувала код до структури свого проєкту, виправила помилки збірки, самостійно виконала запити, перевірила результати та сформулювала висновки | `dotnet build`, `dotnet test`, ручні запити A–E і T-01…T-09, журнал API, `git diff` між vulnerable і fixed commit |

## 7. Передавання

- Remote доступний викладачеві або bundle перевірено через `verify`/`list-heads`: <результат>.
- У Moodle файли/докази видимі й роботу надіслано: <так/ні>.
- Git diff та історію перевірено на секрети: `.env`, паролів, токенів, cookies і дампів БД у репозиторії немає.

## 8. Докази (текстові фрагменти)

**1. T-01.**

```
POST /api/incidents → HTTP/1.1 201 Created
Location: /api/incidents/86939580-82f2-450f-9820-177ff1c6565e
{ "id": "86939580-82f2-450f-9820-177ff1c6565e", "title": "Lab2 коректний інцидент", "severity": "Low", "status": "New", "occurredAtUtc": "2026-10-01T10:00:00+00:00", "createdAtUtc": "2026-11-02T13:42:19.7687263+00:00", "updatedAtUtc": "2026-11-02T13:42:19.7687263+00:00" }
```

**2. T-02.**

```
severity "7"          → 400, Content-Type: application/problem+json, errors.severity: ["Допустимі значення: Low, Medium, High, Critical."]
title "   "           → 400, errors.title
без occurredAtUtc     → 400, errors.occurredAtUtc: ["Час події обов'язковий."]
occurredAtUtc 2099…   → 400, errors.occurredAtUtc: ["Час події не може бути в майбутньому."]
```

**3. T-09 і CP-01.**

```
High, description 39 символів → 400, errors.description: ["Для High/Critical опис має містити щонайменше 40 символів."]
High, description 40 символів → 201, id ec6817c2-c106-4c25-aedd-7327df0ba3e8
POST із id, ownerUserId, status "Closed" → 201, id b7a2265d-dc20-407e-81c8-c12f0e81fd3c
GET /api/incidents/b7a2265d-dc20-407e-81c8-c12f0e81fd3c → 200: id = b7a2265d-…, status = New, ownerDisplayName = Аліса Коваль
```

**4. T-03.**

```
повторний POST з тим самим title → HTTP/1.1 409 Conflict, application/problem+json, "title": "Конфлікт", "detail": "Активний інцидент із таким заголовком уже існує."
```

**5. T-06.**

```
GET /api/incidents/99999999-9999-9999-9999-999999999999 → 404, application/problem+json, "title": "Інцидент не знайдено"
```

**6. S-01 (vulnerable commit b2f04c6).**

```
A  q=USB                      → 200, 1 запис
B  q=zz-no-match              → 200, []
C  контрольний сценарій       → 200, 5 записів (20000000-…-0001 … 0005)
D  q=O%27Brien                → 500
E  q=комп'ютерного (%27)      → 500
Журнал D: Failed executing DbCommand [Parameters=[], CommandType='Text', CommandTimeout='30']
          SELECT * FROM incidents WHERE title ILIKE '%O'Brien%' OR description ILIKE '%O'Brien%' ORDER BY created_at_utc DESC LIMIT 50
          Npgsql.PostgresException: 42601: syntax error at or near "Brien"
Журнал E: Parameters=[] … 42601: syntax error at or near "ютерно"
```

**7. S-02, T-04 (fixed commit).**

```
A  q=USB                      → 200, 1 запис («Перевірка USB навчальної мережі»)
B, C                          → 200, []
D  q=O%27Brien                → 200, «O'Brien classroom report»
E  q=комп'ютерного            → 200, «Перевірка журналу комп'ютерного класу»
q=%25                         → 200, []
Журнал: Executed DbCommand [Parameters=[@pattern='?', @pattern0='?', @p='?' (DbType = Int32)], CommandType='Text', CommandTimeout='30']
        … WHERE i.title ILIKE @pattern ESCAPE '\' OR i.description ILIKE @pattern0 ESCAPE '\' ORDER BY i.created_at_utc DESC, i.id LIMIT @p
Порядок без sortBy: 0004 → 0005 → 0003 → 0002 → 0001
sortBy=severity:    0002 → 0001 → 0003, 0004, 0005
sortBy=status:      0003, 0004, 0005 → 0001 → 0002
```

**8. T-05.**

```
GET /api/incidents/search?sortBy=price → 400, application/problem+json, errors.sortBy: ["Допустимі значення: createdAtUtc, severity, status."]
```

**9. A-01 і security diff.**

```
git grep -n -e FromSqlRaw -e ExecuteSqlRaw -e "ORDER BY" -- src tests (fixed):
  src/SecureLab.Api/Data/DatabaseBootstrap.cs:29: await dbContext.Database.ExecuteSqlRawAsync(
data-access-scan.py: src/SecureLab.Api/Data/DatabaseBootstrap.cs:29: await dbContext.Database.ExecuteSqlRawAsync(
git grep … b2f04c6:
  DatabaseBootstrap.cs:29: ExecuteSqlRawAsync(
  Lab02Endpoints.cs:22: … ORDER BY " + order + " LIMIT 50";
  Lab02Endpoints.cs:23: var rows = await db.Incidents.FromSqlRaw(sql).AsNoTracking().ToListAsync(ct);
git diff --stat b2f04c6..871e497: Lab02Endpoints.cs (57), IncidentContractTests.cs (65), lab-02-checks.http (32); 3 files changed, 135 insertions(+), 19 deletions(-)
Видалено: конкатенацію `var sql = …`, `FromSqlRaw`, гілку `_ => sortBy`.
Додано: `EscapeLike`, `EF.Functions.ILike(..., pattern, "\\")`, allowlist-`switch`, `ValidationProblem` для sortBy.
```

**10. Тести.**

```
PASSED: Post_WithInvalidSeverity_Returns400WithSeverityKey (1782ms)
PASSED: Post_DuplicateActiveTitle_Returns409 (275ms)
Total Tests: 2, Passed: 2, Failed: 0, Skipped: 0 — Result: SUCCEEDED
```
