# Карта архітектури

Це початкова карта. Під час ЛР 1 доповніть її власним трасуванням запиту,
конкретними файлами та спостереженнями з DevTools і журналу PostgreSQL.

## Компоненти

| Компонент      | Розташування                | Відповідальність                                                  |
| -------------- | --------------------------- | ----------------------------------------------------------------- |
| Browser client | `src/SecureLab.Api/Client/` | Надсилає HTTP-запити, безпечно показує відповідь через DOM API    |
| Presentation   | `Presentation/`             | Описує endpoints, читає зовнішні параметри, формує HTTP-відповідь |
| Application    | `Application/`              | Виконує сценарій отримання списку або деталей інциденту           |
| Data           | `Data/`                     | Відображає C#-сутності на PostgreSQL через EF Core/Npgsql         |
| PostgreSQL     | `infra/compose.yaml`        | Зберігає навчальні дані у локальному контейнері                   |

### Досліджений маршрут: GET /api/incidents/{id}

```text
клік на картку інциденту у Client/app.js (loadIncidentDetails)
  → GET /api/incidents/{id}
  → IncidentEndpoints.GetDetailsAsync (Presentation/Endpoints/IncidentEndpoints.cs)
  → IncidentQueries.GetDetailsAsync (Application/Incidents/IncidentQueries.cs)
  → SecureLabDbContext.Incidents (Data/SecureLabDbContext.cs)
  → PostgreSQL: таблиця incidents
  → IncidentDetailsResponse (Presentation/Contracts/IncidentResponses.cs)
  → JSON response
  → renderIncidentDetails: textContent/createTextNode у Client/app.js
```

### Реалізований маршрут: GET /api/incidents/severity-summary

```text
клік на кнопку "Показати підсумок" у Client/app.js (loadSeveritySummary)
  → GET /api/incidents/severity-summary
  → IncidentEndpoints.GetSeveritySummaryAsync (Presentation/Endpoints/IncidentEndpoints.cs)
  → IncidentQueries.GetSeveritySummaryAsync (Application/Incidents/IncidentQueries.cs)
  → SecureLabDbContext.Incidents (Data/SecureLabDbContext.cs)
  → PostgreSQL: таблиця incidents
  → IncidentSeveritySummaryResponse (Presentation/Contracts/IncidentResponses.cs)
  → JSON response
  → renderSeveritySummary: textContent у Client/app.js
```

Обрана політика нульових груп: **лише наявні групи** (порожні рівні Severity не доповнюються нулями).
Порядок елементів: **лексикографічний** (сортування за рядковим значенням Severity).

## Межі довіри

| Межа                        | Чому даним ще не можна довіряти                                                                               | Де перевіряємо або обмежуємо                                                                                                                                                                |
| --------------------------- | ------------------------------------------------------------------------------------------------------------- | ------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------- |
| Користувач → Browser client | Користувач контролює введення                                                                                 | Клієнтські обмеження (наприклад, `<select>` статусу) лише формують зручний UI, самі по собі нічого не гарантують                                                                            |
| Browser client → API        | Клієнт і HTTP-запит можна змінити поза UI (наприклад, через `fetch` у Console з довільним id)                 | Маршрутне обмеження `:guid` на `IncidentEndpoints`, перевірка `Enum.TryParse`/`Enum.IsDefined` для параметра `status`, повернення 400/404 Problem Details                                   |
| API → PostgreSQL            | Право виконати запит не означає, що будь-які умови запиту безпечні                                            | Параметризовані запити через EF Core (`Where`, `GroupBy`), `AsNoTracking()` для read-only сценаріїв                                                                                         |
| PostgreSQL → API → DOM      | У БД може зберігатися раніше введений недовірений текст (наприклад, seed-інцидент з `<script>` у description) | Окремі response DTO (`IncidentDetailsResponse`, `IncidentSeveritySummaryResponse`) без зайвих полів entity; вивід у DOM лише через `textContent`/`document.createTextNode`, без `innerHTML` |

## Конфігураційні входи

- `global.json` — версія .NET SDK;
- `src/SecureLab.Api/appsettings*.json` — режим міграцій і локальний connection string;
- `infra/compose.yaml` — версія PostgreSQL, порт і локальні навчальні облікові дані;
- змінна середовища `ConnectionStrings__SecureLab` — безпечний спосіб перевизначити connection string поза репозиторієм.
