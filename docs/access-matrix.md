# Матриця контролю доступу (baseline 2-A, ЛР 4, рівень «Добре»)

Політика відмов: для чужого головного ресурсу API повертає **404**, ідентичний відповіді для неіснуючого id
(однакові status, Content-Type і тіло; відрізняється лише traceId). Обґрунтування: зменшити розкриття існування ресурсу.
Політика однакова для details, update, delete, triage і child endpoint. Reporter-власник або Analyst, які бачать
ресурс, але не мають права на дію, отримують 403.

| Дія                                    | Анонім    | Reporter-owner                          | Інший Reporter | Analyst                               | Administrator            | Власник має значення?           | Реалізація / тест                                  |
| -------------------------------------- | --------- | --------------------------------------- | -------------- | ------------------------------------- | ------------------------ | ------------------------------- | -------------------------------------------------- |
| Список GET /api/incidents              | deny, 401 | лише власні                             | лише власні    | усі                                   | лише власні (без обходу) | так для Reporter                | IncidentQueries.GetListAsync (query scoping); A-05 |
| Деталі GET /api/incidents/{id}         | deny, 401 | allow                                   | deny, 404      | allow                                 | deny, 404                | так                             | IncidentOperations.Read; A-01, A-02, A-03, A-03R   |
| Створення POST /api/incidents          | deny, 401 | allow for self                          | allow for self | allow for self                        | allow for self           | сервер призначає owner          | успадковано з ЛР 3                                 |
| Update content PUT /api/incidents/{id} | deny, 401 | allow                                   | deny, 404      | deny, 403                             | deny, 403                | так                             | IncidentOperations.Update; A-04, A-08a             |
| Delete DELETE /api/incidents/{id}      | deny, 401 | allow                                   | deny, 404      | deny, 403                             | deny, 403                | так                             | IncidentOperations.Delete; A-04                    |
| Triage POST /api/incidents/{id}/triage | deny, 401 | deny, 403                               | deny, 404      | allow лише New -> Triaged, інакше 409 | deny, 403                | ні                              | IncidentOperations.Triage; A-06, A-07, A-09        |
| Internal comment (details)             | deny, 401 | не бачить                               | не бачить      | бачить                                | не бачить                | ні; parent перевіряється окремо | ReadInternalComments; A-12                         |
| Child GET /api/incident-comments/{id}  | deny, 401 | allow (не internal), internal -> 404    | deny, 404      | allow                                 | deny, 404                | через parent                    | GetCommentAsync; A-12                              |
| Search, severity-summary               | deny, 401 | не обмежується (відмінний рівень, A-11) | -              | -                                     | -                        | -                               | поза рівнем «Добре»                                |

Administrator не має прав «на все»: прямо опублікованих додаткових прав у предметному правилі baseline 2-A немає.
Роль береться лише з server-issued role claim у HttpContext.User.
