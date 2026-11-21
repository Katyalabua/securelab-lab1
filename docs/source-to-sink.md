# Source-to-sink: Incident.Description та IncidentComment.Text (ЛР 5)

## 1. Область і мета

Мета: прослідкувати шлях недовіреного тексту від джерела (HTTP-запит) до sink у браузері (DOM) для двох полів: `Incident.Description` і `IncidentComment.Text`. Стенд: baseline 2-A «Трекер інцидентів», ASP.NET Core + PostgreSQL. Стан «до» зафіксований на vulnerable-коміті (scaffold `022455a` та seed-коміт `d6fd094`), стан «після» на коміті `ced04db` під тегом `v0.5.0`.

Типи джерел: `Incident.Description` створюється користувачем через `POST /api/incidents`; `IncidentComment.Text` потрапляє в базу через endpoint коментарів і через seed (marker-коментар Morgan).

## 2. Карта для Incident.Description

| Крок                     | Місце                            | Файл і рядок                                        | Що відбувається                                                          | Контроль на цьому кроці                  |
| ------------------------ | -------------------------------- | --------------------------------------------------- | ------------------------------------------------------------------------ | ---------------------------------------- |
| Source                   | клієнт або будь-який HTTP-клієнт | `POST /api/incidents`                               | тіло JSON з полем `description`                                          | немає                                    |
| Request                  | маршрут                          | `Lab02Endpoints.cs:50` `MapPost("/api/incidents")`  | endpoint приймає запит                                                   | автентифікація (ЛР 3)                    |
| DTO                      | модель запиту                    | `Lab02Endpoints.cs:138` `CreateIncidentRequest`     | прив'язка JSON до DTO                                                    | немає                                    |
| Validation               | перевірка                        | `Lab02Endpoints.cs:62–66`                           | `description` обов'язкове, довжина ≤ 4000                                | довжина, наявність; HTML не фільтрується |
| Normalization            | обробка                          | `Lab02Endpoints.cs:83` `Trim()`                     | обрізання пробілів                                                       | лише пробіли                             |
| Mapping                  | створення сутності               | `Lab02Endpoints.cs:113`, `Incident.cs:8`            | значення потрапляє в `Incident.Description`                              | немає                                    |
| Storage                  | PostgreSQL                       | `SecureLabDbContext.cs:37`                          | колонка `description`, максимум 4000                                     | параметризований запис (ЛР 2)            |
| Query                    | вибірка                          | `IncidentQueries.cs:51`, `60–63`                    | читання інциденту й коментарів                                           | політика видимості (ЛР 4)                |
| Response DTO             | відповідь                        | `IncidentResponses.cs:14` `IncidentDetailsResponse` | `description` повертається як JSON-рядок без змін                        | JSON-екранування, не HTML                |
| Endpoint                 | читання                          | `IncidentEndpoints.cs:25` `GetDetailsAsync`         | `GET /api/incidents/{id}`                                                | авторизація й ownership (ЛР 4)           |
| Client                   | завантаження                     | `browser-lab.js` функція `load()`                   | `fetch` і `response.json()`                                              | немає                                    |
| Sink (до виправлення)    | DOM                              | `browser-lab.js:10`                                 | `description.innerHTML = current.description`                            | немає                                    |
| Sink (після виправлення) | DOM                              | `browser-lab.js`                                    | `renderText(#description, [current.description])` через `createTextNode` | дані потрапляють лише в текстовий вузол  |

Також оновлення опису: `IncidentEndpoints.cs:169–188` (update path), дані так само зберігаються без HTML-обробки.

## 3. Карта для IncidentComment.Text

| Крок                     | Місце                      | Файл і рядок                                                                                                                                  | Що відбувається                                                            | Контроль                  |
| ------------------------ | -------------------------- | --------------------------------------------------------------------------------------------------------------------------------------------- | -------------------------------------------------------------------------- | ------------------------- |
| Source                   | endpoint коментарів і seed | `IncidentEndpoints.cs:282–306`; `DbSeeder.cs` (marker-коментар Id `30000000-0000-0000-0000-000000000005`, автор Morgan, `IsInternal = false`) | текст коментаря                                                            | довжина ≤ 2000            |
| Storage                  | PostgreSQL                 | `SecureLabDbContext.cs:57`                                                                                                                    | колонка тексту коментаря, максимум 2000                                    | параметризований запис    |
| Query                    | вибірка                    | `IncidentQueries.cs:60–63`                                                                                                                    | коментарі інциденту; внутрішні приховані від Reporter                      | політика видимості (ЛР 4) |
| Response DTO             | відповідь                  | `IncidentResponses.cs:20` `Comments`, `22–25` `IncidentCommentResponse`                                                                       | `text` у JSON без змін                                                     | JSON-екранування          |
| Client                   | завантаження               | `browser-lab.js` `load()`                                                                                                                     | `current.comments`                                                         | немає                     |
| Sink (до виправлення)    | DOM                        | `browser-lab.js:14`                                                                                                                           | `comments.innerHTML` із `comment.text`                                     | немає                     |
| Sink (після виправлення) | DOM                        | `browser-lab.js`                                                                                                                              | `renderText(#comments, тексти, "p")`: `createElement("p")` і `textContent` | дані лише в `textContent` |

Alice (власник, Reporter) бачить лише публічні коментарі, тому marker (`IsInternal = false`) для неї видимий.

## 4. Інвентаризація DOM-sink

| Файл             | Рядок    | Конструкція         | Дані                   | Стан до        | Стан після                                       |
| ---------------- | -------- | ------------------- | ---------------------- | -------------- | ------------------------------------------------ |
| `browser-lab.js` | 10       | `innerHTML`         | `Incident.Description` | уразливий sink | замінено на `renderText` (text node)             |
| `browser-lab.js` | 14       | `innerHTML`         | `IncidentComment.Text` | уразливий sink | замінено на `renderText` (`<p>` + `textContent`) |
| `browser-lab.js` | `#title` | `textContent`       | `Incident.Title`       | безпечний      | без змін                                         |
| `app.js`         | 23–28    | `createTextElement` | дані API               | безпечний      | без змін                                         |

Після виправлення в клієнтському коді немає `innerHTML`, `outerHTML`, `insertAdjacentHTML`, `document.write` для даних із API.

## 5. Гіпотеза

Якщо `Description` або `Text` містять HTML-розмітку, сервер збереже її як звичайний текст і поверне без змін у JSON. Це не уразливість API: на сервері немає виконання. Уразливість виникає на клієнті, де `innerHTML` інтерпретує рядок як розмітку. Тому marker-коментар `<img src=x onerror="…">` виконає JavaScript під origin `http://localhost:5080` при відкритті `/browser-lab.html` (stored XSS, джерело в базі, sink у DOM).

## 6. Висновок про виправлення

Виправлення стоїть у sink, а не в джерелі: клієнт створює структуру коду (`createElement`, `createTextNode`, `textContent`), а дані потрапляють лише в текстові вузли. Валідація HTML на сервері й blacklist-фільтри не застосовувалися, бо форматований HTML у спільному scope не потрібен, а фільтри за списком обходяться. Sanitizer не використано. CSP (`default-src 'self'; frame-ancestors 'none'`) додано як другий рівень, він не замінює виправлення sink.
