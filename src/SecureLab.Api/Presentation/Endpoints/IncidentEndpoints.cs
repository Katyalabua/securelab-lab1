using Microsoft.EntityFrameworkCore;
using SecureLab.Api.Application.Incidents;
using SecureLab.Api.Data;
using SecureLab.Api.Data.Entities;
using SecureLab.Api.Presentation.Contracts;

namespace SecureLab.Api.Presentation.Endpoints;

public static class IncidentEndpoints
{
    public static IEndpointRouteBuilder MapIncidentEndpoints(this IEndpointRouteBuilder endpoints)
    {
        var group = endpoints.MapGroup("/api/incidents")
            .WithTags("Incidents");

        group.MapGet("/", GetListAsync)
            .WithName("GetIncidents")
            .Produces<IReadOnlyList<IncidentListItemResponse>>()
            .ProducesValidationProblem();

        group.MapGet("/{id:guid}", GetDetailsAsync)
            .WithName("GetIncidentDetails")
            .Produces<IncidentDetailsResponse>()
            .ProducesProblem(StatusCodes.Status404NotFound);

        group.MapGet("/severity-summary", GetSeveritySummaryAsync)
            .WithName("GetIncidentSeveritySummary")
            .Produces<IReadOnlyList<IncidentSeveritySummaryResponse>>();

        // VULNERABLE маршрути (лише для PoC)
        group.MapPut("/{id:guid}", VulnerableUpdateAsync).RequireAuthorization();
        group.MapDelete("/{id:guid}", VulnerableDeleteAsync).RequireAuthorization();
        group.MapPost("/{id:guid}/triage", VulnerableTriageAsync).RequireAuthorization();
        endpoints.MapGet("/api/incident-comments/{id:guid}", VulnerableGetCommentAsync).RequireAuthorization();

        return endpoints;
    }

    private static async Task<IResult> GetSeveritySummaryAsync(
        string? status,
        IncidentQueries queries,
        CancellationToken cancellationToken)
    {
        IncidentStatus? parsedStatus = null;
        if (status is not null)
        {
            if (!Enum.TryParse<IncidentStatus>(status, ignoreCase: true, out var candidate)
                || !Enum.IsDefined(candidate))
            {
                return Results.ValidationProblem(new Dictionary<string, string[]>
                {
                    ["status"] = ["Допустимі значення: New, Triaged, InProgress, Resolved, Closed."]
                });
            }

            parsedStatus = candidate;
        }

        var result = await queries.GetSeveritySummaryAsync(parsedStatus, cancellationToken);
        return Results.Ok(result);
    }

    private static async Task<IResult> GetListAsync(
        string? status,
        IncidentQueries queries,
        CancellationToken cancellationToken)
    {
        IncidentStatus? parsedStatus = null;
        if (status is not null)
        {
            if (!Enum.TryParse<IncidentStatus>(status, ignoreCase: true, out var candidate)
                || !Enum.IsDefined(candidate))
            {
                return Results.ValidationProblem(new Dictionary<string, string[]>
                {
                    ["status"] = ["Допустимі значення: New, Triaged, InProgress, Resolved, Closed."]
                });
            }

            parsedStatus = candidate;
        }

        return Results.Ok(await queries.GetListAsync(parsedStatus, cancellationToken));
    }

    private static async Task<IResult> GetDetailsAsync(
        Guid id,
        IncidentQueries queries,
        CancellationToken cancellationToken)
    {
        var incident = await queries.GetDetailsAsync(id, cancellationToken);
        return incident is null
            ? Results.Problem(
                title: "Інцидент не знайдено",
                detail: $"Інцидент '{id}' не існує.",
                statusCode: StatusCodes.Status404NotFound)
            : Results.Ok(incident);
    }

    private static async Task<IResult> VulnerableUpdateAsync(
        Guid id, UpdateIncidentRequest request, SecureLabDbContext db, CancellationToken ct)
    {
        var incident = await db.Incidents.SingleOrDefaultAsync(i => i.Id == id, ct);
        if (incident is null) return Results.NotFound();
        incident.Title = request.Title;
        incident.Description = request.Description;
        if (request.OwnerUserId is not null) incident.OwnerUserId = request.OwnerUserId.Value;
        if (request.Status is not null
            && Enum.TryParse<IncidentStatus>(request.Status, true, out var st)) incident.Status = st;
        await db.SaveChangesAsync(ct);
        return Results.NoContent();
    }

    private static async Task<IResult> VulnerableDeleteAsync(
        Guid id, SecureLabDbContext db, CancellationToken ct)
    {
        var incident = await db.Incidents.SingleOrDefaultAsync(i => i.Id == id, ct);
        if (incident is null) return Results.NotFound();
        db.Incidents.Remove(incident);
        await db.SaveChangesAsync(ct);
        return Results.NoContent();
    }

    private static async Task<IResult> VulnerableTriageAsync(
        Guid id, SecureLabDbContext db, CancellationToken ct)
    {
        var incident = await db.Incidents.SingleOrDefaultAsync(i => i.Id == id, ct);
        if (incident is null) return Results.NotFound();
        incident.Status = IncidentStatus.Triaged;
        await db.SaveChangesAsync(ct);
        return Results.NoContent();
    }

    private static async Task<IResult> VulnerableGetCommentAsync(
        Guid id, SecureLabDbContext db, CancellationToken ct)
    {
        var comment = await db.IncidentComments.AsNoTracking()
            .Where(c => c.Id == id)
            .Select(c => new IncidentCommentResponse(
                c.Id, c.Author.DisplayName, c.Text, c.CreatedAtUtc))
            .SingleOrDefaultAsync(ct);
        return comment is null ? Results.NotFound() : Results.Ok(comment);
    }
}
