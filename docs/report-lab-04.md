# Звіт ЛР04

## Контекст і межа виконання

- Рівень: **добрий («Добре»)**. Виконано весь достатній рівень (головна action details/read, entry-контракт ЛР 5, один IDOR/BOLA-сценарій, A-02, A-03, A-03R, browser client, тег) і додатково повний перелік доброго рівня: list, update, delete, triage, child endpoint, internal comment, audit, A-01, A-04–A-07, A-08a, A-09, A-10, A-12. Відмінний рівень (A-11) не виконувався.
- Власний baseline 2-A «Трекер інцидентів». Base tag `v0.3.0`, hash `b28d36c8534ef1cf3896607e033d1c193b5510c5`. Гілка `lab/4-authorization`.
- Головна action: `GET /api/incidents/{id}` (details/read). Політика для чужого ресурсу: **404**. Маршрути рівня: `GET /api/incidents`, `GET /api/incidents/{id}`, `PUT /api/incidents/{id}`, `DELETE /api/incidents/{id}`, `POST /api/incidents/{id}/triage`, `GET /api/incident-comments/{id}`. Створення (`POST /api/incidents`) успадковано з ЛР 3.
- ЛР 3 після тегу `v0.3.0` не виправлялась, тег не пересувався.

| Стан                                               | Commit                                     |
| -------------------------------------------------- | ------------------------------------------ |
| Vulnerable (IDOR PoC)                              | `18ef7cfc431878f8d8753e8e07694323dfe30188` |
| Виправлення коду                                   | `a47912cc04ac1d06cd53af947a8f25aa050a7ecb` |
| Fixed (код, тести, матриця; на ньому тег `v0.4.0`) | `88cee25b5b9a4b6d422bac4005ac0e12ee2a750d` |

Усі експерименти виконано лише локально, на штучних облікових записах Alice, Bob, Morgan, Admin та синтетичних даних baseline 2-A. Рядки й розділи відмінного рівня позначено «не виконувалось — рівень відмінний».

## Матриця і server-side рішення

Повна матриця: `docs/access-matrix.md`.

Політика відмов: для чужого головного ресурсу API повертає **404**, ідентичний відповіді для неіснуючого id (однакові status, Content-Type і тіло, відрізняється лише `traceId`). Обґрунтування: не розкривати сам факт існування чужого інциденту. Політика однакова для details, update, delete, triage і child endpoint. Якщо суб'єкт бачить ресурс, але не має права на дію (Reporter-власник виконує triage, Analyst виконує update або delete), повертається 403, бо існування ресурсу йому вже відоме.

| Дія                            | Анонім    | Reporter-owner      | Інший Reporter | Analyst                              | Administrator  |
| ------------------------------ | --------- | ------------------- | -------------- | ------------------------------------ | -------------- |
| Список                         | deny, 401 | лише власні         | лише власні    | усі                                  | лише власні    |
| Деталі                         | deny, 401 | allow               | deny, 404      | allow                                | deny, 404      |
| Створення                      | deny, 401 | allow for self      | allow for self | allow for self                       | allow for self |
| Update                         | deny, 401 | allow               | deny, 404      | deny, 403                            | deny, 403      |
| Delete                         | deny, 401 | allow               | deny, 404      | deny, 403                            | deny, 403      |
| Triage                         | deny, 401 | deny, 403           | deny, 404      | allow лише New → Triaged, інакше 409 | deny, 403      |
| Internal comment               | deny, 401 | не бачить           | не бачить      | бачить                               | не бачить      |
| Child `incident-comments/{id}` | deny, 401 | allow (не internal) | deny, 404      | allow                                | deny, 404      |

Administrator не має прав «на все»: прямо опублікованих додаткових прав у baseline 2-A немає. Роль береться лише з server-issued role claim у `HttpContext.User`, а не з body, query чи заголовка.

Де реалізовано:

- **Resource-based handler:** `IncidentAuthorizationHandler` з операціями Read, Update, Delete, Triage, ReadInternalComments (`Application/Incidents/IncidentAuthorization.cs`); зареєстрований у `Program.cs` як `IAuthorizationHandler`. Handler отримує `ClaimsPrincipal`, завантажену entity `Incident` і операцію; owner визначається порівнянням claim `NameIdentifier` з `OwnerUserId`, роль перевіряється через `IsInRole`.
- **Query scoping:** `IncidentQueries.GetListAsync`: умова за власником або роллю Analyst стоїть у самому запиті, `ownerId` і роль endpoint бере з principal.
- **Internal-коментарі:** `GetDetailsAsync` додає їх лише коли дозволено `ReadInternalComments`.
- **DTO:** `UpdateIncidentRequest` містить лише title, description, severity, occurredAtUtc; `OwnerUserId`, `Status`, role у контракті немає.
- **Server-managed state:** owner, status і `UpdatedAtUtc` встановлює сервер; наступний статус triage (`Triaged`) визначається після перевірки поточного стану.

## Вразливий стан і PoC

Vulnerable commit: `18ef7cfc431878f8d8753e8e07694323dfe30188`. Ресурсна перевірка в ньому відсутня, mutating endpoint-и завантажують інцидент лише за `Id`, група `/api/incidents` не вимагала автентифікації. Сценарій: `tests/http/lab-04-access.http`, один і той самий cookie Alice, змінено лише identifier. Cookie й пароль у доказах немає.

| Дія                                                                                      | Очікування за матрицею  | Фактичний status у vulnerable стані                                       |
| ---------------------------------------------------------------------------------------- | ----------------------- | ------------------------------------------------------------------------- |
| Alice → власний details `…0001`                                                          | allow                   | 200                                                                       |
| Alice → details Bob `…0002`                                                              | deny                    | **200 з даними Bob** (назва «Невідома спроба входу», автор «Боб Мельник») |
| Alice → update інциденту Bob                                                             | deny                    | 204; повторне читання показує назву «ЗМІНЕНО Alice»                       |
| Alice → update власного `…0003` з forged `ownerUserId` (Morgan) і `status: Triaged`      | змінюється лише content | 204; власник став «Морган Литвин», статус `Triaged`                       |
| Alice (Reporter) → triage власного `…0005`                                               | deny                    | 204; статус `Triaged` (вертикальне підвищення привілеїв)                  |
| Alice → коментар Morgan до інциденту Bob `…0002`                                         | deny                    | 200                                                                       |
| Alice → internal comment `…0003`                                                         | deny                    | 200 (внутрішній текст витікає)                                            |
| Alice → delete інциденту Bob `…0004`                                                     | deny                    | 204; Bob потім отримує 404                                                |
| Анонімний details                                                                        | deny, 401               | 200 (маршрут не вимагав login)                                            |
| Browser: `loadIncidentDetails("20000000-0000-0000-0000-000000000002")` після login Alice | deny                    | у блоці «Деталі» відображаються дані Bob                                  |

Окреме спостереження: details відповідав і без cookie, тобто маршрут додатково не вимагав автентифікації.

## Виправлення та retest

Fixed commit (на ньому тег `v0.4.0`): `88cee25b5b9a4b6d422bac4005ac0e12ee2a750d`; виправлення коду: `a47912cc04ac1d06cd53af947a8f25aa050a7ecb`.

Порядок endpoint-ів update, delete і triage: завантажити tracked entity (без `AsNoTracking()`) → рішення Read (відмова = той самий 404, що й для неіснуючого id) → рішення потрібної дії (відмова = 403) → для triage перевірка поточного статусу (лише `New`, інакше 409) → сервер сам встановлює наступний стан → `SaveChangesAsync`. Зміна стану відбувається лише після дозволу.

Інші зміни:

- група `/api/incidents` і `/api/incident-comments/{id}` отримали `RequireAuthorization()`; анонімний запит повертає 401, а не HTML redirect;
- details повертається лише після рішення Read; відповідь не містить `OwnerUserId` чи email;
- child endpoint завантажує коментар разом із parent incident, перевіряє Read для parent, а для internal-коментаря ще й `ReadInternalComments`; будь-яка відмова повертає той самий 404;
- у `UpdateIncidentRequest` немає owner, status і role (A-08a: невідомі поля ігноруються).

Retest: ті самі запити після reset.

| Запит                              | До                       | Після                                                                         |
| ---------------------------------- | ------------------------ | ----------------------------------------------------------------------------- |
| Alice → власний details            | 200                      | 200, internal-коментаря немає                                                 |
| Alice → details Bob                | 200 з даними Bob         | **404** (`application/problem+json`, «Інцидент не знайдено»), даних Bob немає |
| Alice → неіснуючий id              | 404 (з detail)           | така сама 404, відрізняється лише `traceId`                                   |
| Alice → update і delete Bob        | 204                      | 404; назва Bob «Невідома спроба входу» незмінна                               |
| Alice → forged update власного     | змінились owner і status | 204; змінився лише content, owner і status `New` незмінні                     |
| Alice (Reporter) → triage власного | 204                      | **403**; статус `New`; у терміналі API `authorization.denied`                 |
| Alice → коментарі `…0002`, `…0003` | 200                      | 404 обидва                                                                    |
| Alice → список                     | усі інциденти            | лише `…0001`, `…0003`, `…0005`                                                |
| Morgan → details Alice             | 200                      | 200 з internal-коментарем                                                     |
| Morgan → triage інциденту `New`    | 204                      | 204, статус `Triaged`                                                         |
| Анонімний details                  | 200                      | 401                                                                           |

Позитивна регресія: owner (Alice, Bob) читає й змінює власні інциденти; Analyst (Morgan) читає чужі інциденти разом з internal-коментарями та виконує дозволений перехід New → Triaged, але update і delete йому заборонено (403). Для mutation стан до і після відмови порівнюється повторним читанням дозволеним клієнтом (A-04, A-06, A-08a). Для read відповідь не розкриває чужих даних (A-03). Однаковість 404 для чужого й неіснуючого ресурсу підтверджено окремим тестом A-10.

## Доказова таблиця

Автоматичні HTTP-тести: `tests/SecureLab.Api.Tests/AuthorizationTests.cs`. Клієнти alice, bob, morgan створюються один раз на клас (обмежувач спроб входу з ЛР 3 не пропускає десятки логінів). Mutating-тести працюють із власноруч створеним інцидентом і прибирають його у `finally`. Негативні тести порівнюють стан до і після відмови.

| ID    | Рівень                         | Сценарій                                    | Передумови                                                                           | Дія                                                                    | Очікувано                                                  | Фактично                                                                                                                                     | Доказ                                                                                                            |
| ----- | ------------------------------ | ------------------------------------------- | ------------------------------------------------------------------------------------ | ---------------------------------------------------------------------- | ---------------------------------------------------------- | -------------------------------------------------------------------------------------------------------------------------------------------- | ---------------------------------------------------------------------------------------------------------------- |
| A-01  | добрий                         | Анонімний доступ                            | немає authentication context                                                         | `GET /api/incidents/{aliceId}` без cookie                              | `401`, не HTML redirect                                    | 401, `application/problem+json`, redirect відсутній                                                                                          | тест `Details_Anonymous_Returns401`                                                                              |
| A-02  | достатній, обов’язковий        | Власний ресурс                              | Alice має власний seed resource                                                      | `GET /api/incidents/{aliceId}`                                         | allow                                                      | 200, id `…0001`, internal-коментаря немає                                                                                                    | тест `Details_AsOwner_ReturnsOk`                                                                                 |
| A-03  | достатній, обов’язковий        | Чужий ресурс                                | Alice, Bob resource                                                                  | `GET /api/incidents/{bobId}`                                           | `403` або `404`; дані Bob не розкриті                      | 404, у відповіді немає даних Bob                                                                                                             | тест `Details_AsOtherReporter_Returns404_WithoutData`                                                            |
| A-03R | достатній, entry-контракт ЛР 5 | Дозволений аналітичний read                 | Morgan (`Analyst`), ресурс Alice                                                     | `GET /api/incidents/{aliceId}`                                         | allow для аналізу; не надає права довільної mutation       | 200 з internal-коментарем; update від Morgan → 403                                                                                           | тести `Details_AsAnalyst_ReturnsOk_WithInternalComment`, `Update_AsAnalyst_Returns403_AndDoesNotChangeResource`  |
| A-04  | добрий                         | Чужий ресурс, усі змінювальні дії           | Alice, Bob resource                                                                  | `PUT` і `DELETE` з id Bob                                              | deny, state без змін                                       | 404 для обох; Bob після цього читає свій інцидент, назва незмінна                                                                            | тести `Update_AsNonOwner_Returns404_AndDoesNotChangeResource`, `Delete_AsNonOwner_Returns404_AndResourceRemains` |
| A-05  | добрий                         | List                                        | Alice                                                                                | `GET /api/incidents`                                                   | лише дозволена множина                                     | три власні інциденти, інциденту Bob немає                                                                                                    | тест `List_AsReporter_DoesNotContainForeignIncidents`                                                            |
| A-06  | добрий                         | Вертикальне підвищення привілеїв            | звичайний Reporter                                                                   | `POST /api/incidents/{id}/triage`                                      | deny, status без змін                                      | 403, статус лишився `New`                                                                                                                    | тест `Triage_AsReporter_Returns403_AndStatusUnchanged`                                                           |
| A-07  | добрий                         | Дозволена привілейована дія                 | Morgan (`Analyst`), інцидент у статусі `New`                                         | `POST /api/incidents/{id}/triage`                                      | allow                                                      | 204, статус став `Triaged`                                                                                                                   | тест `Triage_AsAnalyst_TriagesNewIncident`                                                                       |
| A-08a | добрий, альтернатива A-08b     | Overposting: контракт ігнорує невідоме поле | authenticated user; DTO не містить protected field                                   | PUT із forged `ownerUserId`, `status`, `role`                          | дозволений content може змінитися, protected state — ні    | 204; title змінився, власник і статус `New` незмінні                                                                                         | тест `Update_WithForgedFields_DoesNotChangeOwnerOrStatus`                                                        |
| A-08b | добрий, альтернатива A-08a     | Overposting: контракт відхиляє зайве поле   | —                                                                                    | —                                                                      | —                                                          | не виконувалось — обрано альтернативу A-08a                                                                                                  | —                                                                                                                |
| A-09  | добрий                         | Аудиторська подія                           | denied privileged request                                                            | знайти `authorization.denied` у виводі API                             | безпечна подія без секретів                                | подія містить subject id, action `incident.triage`, resource, result `denied`; збігається з `traceId` у тілі 403; пароля, cookie, body немає | рядок із терміналу API разом із відповіддю 403 (розділ «Аудит» нижче)                                            |
| A-10  | добрий, лише політика `404`    | Нерозрізнюваність `404`                     | той самий authenticated client                                                       | два запити, що відрізняються лише id: чужий `…0002` і `Guid.NewGuid()` | однакові status, `Content-Type` і тіло без `traceId`       | 404 / `application/problem+json` / однакове тіло, відрізняється лише `traceId`                                                               | тест `Details_ForeignAndUnknown_AreIndistinguishable`                                                            |
| A-11  | відмінний                      | Власні endpoint-и ЛР 1–2                    | —                                                                                    | —                                                                      | —                                                          | не виконувалось — рівень відмінний                                                                                                           | —                                                                                                                |
| A-12  | добрий                         | Child resource та internal comment          | Alice (`Reporter`); id коментаря Morgan до інциденту Bob (`…0002`); internal `…0003` | `GET /api/incident-comments/{id}`; details Alice                       | direct child access — deny; internal не потрапляє Reporter | 404 для обох коментарів; у details Alice internal-коментаря немає; Morgan бачить internal                                                    | тести `Comment_OfForeignIncident_Returns404`, `InternalComment_IsHiddenFromReporter_AndVisibleToAnalyst`         |

Підсумок: `dotnet test` завершується без failed, у моєму останньому запуску всього тестів `35`. Три тести `IncidentEndpointTests`, успадковані з ЛР 1, переведено на клієнта після login Alice (після `RequireAuthorization()` вони падали з 401); `GetList_ReturnsSeededIncidents` замінено перевіркою A-05. Це зміна контракту за матрицею, а не послаблення перевірки.

## Основний finding і залишковий ризик

**Контекст.** Ресурс Incident; дії details, update, delete, triage, child comment; суб'єкти Alice і Bob (Reporter), Morgan (Analyst).

**Гіпотеза.** Alice, автентифікована як Reporter, зможе прочитати й змінити ресурс Bob підстановкою його identifier і виконати triage без права на нього.

**Стан до.** Vulnerable commit `18ef7cfc431878f8d8753e8e07694323dfe30188`: details Bob від Alice — 200 з чужими даними; update, delete і triage проходили; forged owner/status приймався; internal-коментарі віддавались.

**Першопричина.** Сервер не ухвалював рішення авторизації для пари «суб'єкт + дія + ресурс»: запити шукали інцидент лише за `Id`, без порівняння `OwnerUserId` із principal; група `/api/incidents` не мала `RequireAuthorization()`; update-DTO приймав `OwnerUserId` і `Status`; triage не перевіряв роль; internal-коментарі фільтрувались однаково для всіх ролей; child endpoint шукав коментар лише за його id без перевірки права на parent. Передача id в URL дефектом не є.

**Виправлення.** Централізований resource-based handler, query scoping для list, DTO без protected-полів, `RequireAuthorization()` на групі та child endpoint, один helper 404, audit відмови triage.

**Retest.** Ті самі запити після виправлення не дають доступу й не змінюють стан (таблиця вище, A-01…A-12).

**Позитивна регресія.** Owner і Analyst виконують передбачені матрицею дії (A-02, A-03R, A-07).

**Схема рішення.** Запит Alice з id → cookie формує перевірений principal → завантаження entity → resource-based handler (owner, role) → дозвіл або погоджена відмова 404/403 → відповідь; mutation лише після дозволу.

**Залишковий ризик.**

- CSRF для cookie-автентифікованих mutating endpoint не усунено: це явно задокументований навчальний борг ЛР 3, закриття до ЛР 6.
- `search` і `severity-summary` на рівні «Добре» лише закриті від анонімів; їх обмеження за власником (A-11) належить до відмінного рівня.
- Перевірка одночасних переходів стану (concurrent transitions) не виконувалась: сценарій від викладача не надавався.
- Administrator не має додаткових прав, окрім опублікованих у baseline 2-A.

## Browser flow

Клієнт показує лише рішення сервера і не є авторизацією; відмова формується через `textContent`, а не `innerHTML`, `app.js` не змінювався. Credentials, cookie і Set-Cookie не наводяться.

1. Login Alice на `http://localhost:5080/auth-lab.html` → головна сторінка. У списку три власні інциденти. Деталі власного інциденту «Підозрілий лист із вкладенням» відображаються без internal-коментаря.
2. Виклик `loadIncidentDetails("20000000-0000-0000-0000-000000000002")` (id Bob) після login Alice дає в блоці «Деталі» повідомлення «Помилка: Інцидент не знайдено», однакове для чужого й неіснуючого id.
3. Morgan (Analyst): у списку всі п'ять інцидентів, у деталях інциденту Alice є коментар «Внутрішня нотатка аналітика».

Стан до виправлення: у блоці «Деталі» відображались дані Bob («Невідома спроба входу», автор «Боб Мельник»).

## Добрий рівень: роль, transition, child, audit

**Привілейована дія.** `POST /api/incidents/{id}/triage`, перехід `New → Triaged`, лише Analyst. Роль береться з server-issued role claim, що потрапляє до principal під час login, тому в тестах клієнт Morgan створюється через login. Сервер читає поточний статус із БД, перевіряє, що це `New` (інакше 409), і сам встановлює `Triaged`; тіла запиту немає, тому клієнт не може передати role, owner чи status.

**Transition за ролями.** Reporter-власник — 403 (A-06), інший Reporter — 404, Analyst — 204 і статус `Triaged` (A-07), Administrator — 403.

**Child і internal comment.** Alice → коментар Morgan до інциденту Bob: 404 (A-12). Internal-коментар до інциденту Alice: Reporter — 404 на прямий запит і відсутній у details, Analyst — 200 після авторизації parent incident. Те саме правило застосовано в details-проєкції та в `GET /api/incident-comments/{id}`.

**Аудит.** Після відмови в triage API записує структуровану подію: `authorization.denied`, `subject` (server-verified id з principal), `action=incident.triage`, `resource` (id інциденту), `result=denied`, `occurredAtUtc` (час формує сервер), `traceId`. Значення `traceId` береться виразом `Activity.Current?.Id ?? httpContext.TraceIdentifier`, тому збігається з полем `traceId` у Problem Details відповіді 403. Пароля, cookie, token, повного body і stack trace в події немає. Рішення про відмову ухвалюється всередині endpoint, бо декларативна policy на маршруті не виконала б код endpoint і подію не було б де записати.

```
authorization.denied subject=<id Alice> action=incident.triage resource=20000000-0000-0000-0000-000000000005 result=denied occurredAtUtc=
```

## Відмінний рівень

Не виконувалось — рівень відмінний: A-11 (search і severity-summary), окремий finding додаткової проблеми, відокремлена policy для цих маршрутів, обґрунтування least privilege і залишкового ризику розширеного охоплення. Batch/export endpoint у проєкті немає; concurrent transitions — сценарій від викладача не надавався: не застосовується.

## Перевірки, відтворення і подання

- Середовище: `Microsoft Windows NT 10.0.26200.0`, .NET `10.0.302`, PostgreSQL в Docker, VS Code з REST Client (`tests/http/lab-04-access.http`), Chrome.
- Команди відтворення (з кореня проєкту): `docker compose --env-file infra/.env.example -f infra/compose.yaml up -d --wait`, `dotnet run --project src/SecureLab.Api -- --reset-database`, `dotnet test tests/SecureLab.Api.Tests/SecureLab.Api.Tests.csproj --configuration Release`, `dotnet run --project src/SecureLab.Api --launch-profile http`.
- Результат: reset завершується повідомленням про повторне заповнення seed; `dotnet test` без failed, усього тестів `35`. Після PoC стан відновлюється `--reset-database`.
- Під час тестування виявлено, що обмежувач спроб входу з ЛР 3 відхиляє надто часті login (`rate_limited` у логах API); тести переписано так, щоб клієнти alice, bob, morgan створювались один раз на клас.
- Hash тегу `v0.4.0` = fixed commit `88cee25b5b9a4b6d422bac4005ac0e12ee2a750d` (вписано наступним commit після тегу). Vulnerable commit `18ef7cfc431878f8d8753e8e07694323dfe30188`. Передано: гілка `lab/4-authorization`, тег `v0.4.0`, посилання на private remote, цей звіт у Moodle.
- Не виконувались: відмінний рівень (A-11), A-08b, concurrent transitions. Обмеження: CSRF на cookie mutation (борг до ЛР 6).

## Використання ШІ (довільна форма; журнал — за бажанням)

Використав/ла дозволеним способом.
| Дата/інструмент | Для чого | Що використано | Що перевірено самостійно |
|---|---|---|---|
| 11–15.11.2026, Claude | пояснення методики, заготовки коду і команд, чернетка звіту | схема handler-а, endpoint-и, тести A-01…A-12, команди PowerShell | адаптовано до власних `IncidentQueries`, `SecureLabDbContext`, `CookieClient`; усі результати отримано власними запусками; знайдено і виправлено проблему з обмежувачем входу в тестах; код зібрано, тести запущено, retest виконано вручну |

Паролі, cookie, реальні персональні дані й приватний репозиторій у ШІ не надсилались.
