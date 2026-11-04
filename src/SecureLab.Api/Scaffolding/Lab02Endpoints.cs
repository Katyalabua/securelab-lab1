using Microsoft.EntityFrameworkCore;
using SecureLab.Api.Data;
using SecureLab.Api.Data.Entities;

namespace SecureLab.Api.Scaffolding;

// Навчальний старт ЛР 02. Запускати лише з локальними штучними даними.
public static class Lab02Endpoints
{
    public static void MapLab02Endpoints(this WebApplication app)
    {
        app.MapGet("/api/incidents/search", async (string? q, string? sortBy, SecureLabDbContext db, CancellationToken ct) =>
        {
            // Значення q іде лише в параметр; спецсимволи LIKE екрануємо, щоб % і _ шукалися буквально.
            var pattern = "%" + EscapeLike(q ?? "") + "%";

            var found = db.Incidents.AsNoTracking().Where(i =>
                EF.Functions.ILike(i.Title, pattern, "\\") ||
                EF.Functions.ILike(i.Description, pattern, "\\"));

            // sortBy вибирає лише серверну структуру запиту (allowlist); клієнтський текст в SQL не потрапляє.
            IQueryable<Incident>? ordered = sortBy switch
            {
                null or "" or "createdAtUtc" => found.OrderByDescending(i => i.CreatedAtUtc).ThenBy(i => i.Id),
                "severity" => found.OrderBy(i =>
                    i.Severity == IncidentSeverity.Critical ? 0 :
                    i.Severity == IncidentSeverity.High ? 1 :
                    i.Severity == IncidentSeverity.Medium ? 2 : 3).ThenBy(i => i.Id),
                "status" => found.OrderBy(i =>
                    i.Status == IncidentStatus.New ? 0 :
                    i.Status == IncidentStatus.Triaged ? 1 :
                    i.Status == IncidentStatus.InProgress ? 2 :
                    i.Status == IncidentStatus.Resolved ? 3 : 4).ThenBy(i => i.Id),
                _ => null
            };

            if (ordered is null)
                return Results.ValidationProblem(new Dictionary<string, string[]>
                {
                    ["sortBy"] = ["Допустимі значення: createdAtUtc, severity, status."]
                });

            var rows = await ordered.Take(50).ToListAsync(ct);
            return Results.Ok(rows.Select(row => new IncidentSearchItem(
                row.Id, row.Title, row.Description,
                row.Severity.ToString(), row.Status.ToString(), row.CreatedAtUtc)));
        });

        app.MapPost("/api/incidents", async (CreateIncidentRequest request, SecureLabDbContext db, CancellationToken ct) =>
        {
            // Час фіксуємо один раз на початку обробки.
            var now = DateTimeOffset.UtcNow;
            var errors = new Dictionary<string, string[]>();

            // 1. title: обов'язковий, довжина вхідного значення до Trim() не більша за 160.
            if (string.IsNullOrWhiteSpace(request.Title))
                errors["title"] = ["Заголовок обов'язковий."];
            else if (request.Title.Length > 160)
                errors["title"] = ["Заголовок не довший за 160 символів."];

            // 2. description: обов'язковий, довжина вхідного значення до Trim() не більша за 4000.
            if (string.IsNullOrWhiteSpace(request.Description))
                errors["description"] = ["Опис обов'язковий."];
            else if (request.Description.Length > 4000)
                errors["description"] = ["Опис не довший за 4000 символів."];

            // 3. severity: і перетворення, і належність до набору Low/Medium/High/Critical.
            var severityIsValid =
                Enum.TryParse<IncidentSeverity>(request.Severity, ignoreCase: true, out var severity)
                && Enum.IsDefined(severity);
            if (!severityIsValid)
                errors["severity"] = ["Допустимі значення: Low, Medium, High, Critical."];

            // 4. occurredAtUtc: обов'язкова, не пізніше now + 5 хвилин.
            if (request.OccurredAtUtc is null)
                errors["occurredAtUtc"] = ["Час події обов'язковий."];
            else if (request.OccurredAtUtc > now.AddMinutes(5))
                errors["occurredAtUtc"] = ["Час події не може бути в майбутньому."];

            // 5. Нормалізація один раз; далі працюємо лише з цими змінними.
            var title = request.Title?.Trim() ?? "";
            var description = request.Description?.Trim() ?? "";

            // 6. Cross-field (T-09): для High/Critical опис після Trim() має містити щонайменше 40 символів.
            if (severityIsValid
                && (severity == IncidentSeverity.High || severity == IncidentSeverity.Critical)
                && !errors.ContainsKey("description")
                && description.Length < 40)
                errors["description"] = ["Для High/Critical опис має містити щонайменше 40 символів."];

            // 7. Усі помилки контракту: одна відповідь 400 до звернення до БД.
            if (errors.Count > 0)
                return Results.ValidationProblem(errors);

            // 8. Предметний конфлікт (T-03): той самий title з урахуванням регістру
            //    в статусі New/Triaged/InProgress/Resolved. Closed не блокує.
            if (await db.Incidents.AnyAsync(i => i.Title == title && i.Status != IncidentStatus.Closed, ct))
                return Results.Problem(
                    statusCode: StatusCodes.Status409Conflict,
                    title: "Конфлікт",
                    detail: "Активний інцидент із таким заголовком уже існує.");

            // 9. Entity: server-managed поля встановлює лише сервер.
            var incident = new Incident
            {
                Id = Guid.NewGuid(),
                OwnerUserId = DbSeeder.AliceId,
                Title = title,
                Description = description,
                Severity = severity,
                Status = IncidentStatus.New,
                OccurredAtUtc = request.OccurredAtUtc!.Value.ToUniversalTime(),
                CreatedAtUtc = now,
                UpdatedAtUtc = now
            };
            db.Incidents.Add(incident);
            await db.SaveChangesAsync(ct);

            // 10. Response DTO: лише поля зовнішнього контракту.
            return Results.Created($"/api/incidents/{incident.Id}",
                new CreatedIncidentResponse(
                    incident.Id, incident.Title, incident.Severity.ToString(),
                    incident.Status.ToString(), incident.OccurredAtUtc,
                    incident.CreatedAtUtc, incident.UpdatedAtUtc));
        });
    }

    // Екранування спецсимволів LIKE: спочатку \, потім _ і %.
    private static string EscapeLike(string value) =>
        value.Replace("\\", "\\\\").Replace("_", "\\_").Replace("%", "\\%");
}

public sealed record CreateIncidentRequest(
    string? Title, string? Description, string? Severity, DateTimeOffset? OccurredAtUtc);

public sealed record CreatedIncidentResponse(
    Guid Id, string Title, string Severity, string Status,
    DateTimeOffset OccurredAtUtc, DateTimeOffset CreatedAtUtc, DateTimeOffset UpdatedAtUtc);

public sealed record IncidentSearchItem(
    Guid Id, string Title, string Description, string Severity, string Status, DateTimeOffset CreatedAtUtc);