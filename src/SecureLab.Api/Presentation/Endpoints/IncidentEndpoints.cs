using System.Diagnostics;
using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
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
            .WithTags("Incidents")
            .RequireAuthorization();

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

        group.MapPut("/{id:guid}", UpdateAsync);
        group.MapDelete("/{id:guid}", DeleteAsync);
        group.MapPost("/{id:guid}/triage", TriageAsync);

        endpoints.MapGet("/api/incident-comments/{id:guid}", GetCommentAsync)
            .WithTags("Incidents")
            .RequireAuthorization();

        return endpoints;
    }

    private static IResult IncidentNotFound() => Results.Problem(
        title: "Інцидент не знайдено",
        statusCode: StatusCodes.Status404NotFound);

    private static bool TryParseStatus(string? status, out IncidentStatus? parsed, out IResult? error)
    {
        parsed = null;
        error = null;
        if (status is null)
        {
            return true;
        }

        if (!Enum.TryParse<IncidentStatus>(status, ignoreCase: true, out var candidate)
            || !Enum.IsDefined(candidate))
        {
            error = Results.ValidationProblem(new Dictionary<string, string[]>
            {
                ["status"] = ["Допустимі значення: New, Triaged, InProgress, Resolved, Closed."]
            });
            return false;
        }

        parsed = candidate;
        return true;
    }

    private static async Task<IResult> GetSeveritySummaryAsync(
        string? status,
        IncidentQueries queries,
        CancellationToken cancellationToken)
    {
        if (!TryParseStatus(status, out var parsedStatus, out var error))
        {
            return error!;
        }

        var result = await queries.GetSeveritySummaryAsync(parsedStatus, cancellationToken);
        return Results.Ok(result);
    }

    private static async Task<IResult> GetListAsync(
        string? status,
        HttpContext httpContext,
        IncidentQueries queries,
        CancellationToken cancellationToken)
    {
        if (!TryParseStatus(status, out var parsedStatus, out var error))
        {
            return error!;
        }

        var userIdValue = httpContext.User.FindFirstValue(ClaimTypes.NameIdentifier);
        if (!Guid.TryParse(userIdValue, out var ownerId))
        {
            return Results.Challenge();
        }

        var isAnalyst = httpContext.User.IsInRole(DbSeeder.AnalystRole);
        return Results.Ok(await queries.GetListAsync(parsedStatus, ownerId, isAnalyst, cancellationToken));
    }

    private static async Task<IResult> GetDetailsAsync(
        Guid id,
        HttpContext httpContext,
        IAuthorizationService authorizationService,
        SecureLabDbContext dbContext,
        IncidentQueries queries,
        CancellationToken cancellationToken)
    {
        var incident = await dbContext.Incidents
            .AsNoTracking()
            .SingleOrDefaultAsync(item => item.Id == id, cancellationToken);
        if (incident is null)
        {
            return IncidentNotFound();
        }

        var canRead = await authorizationService.AuthorizeAsync(
            httpContext.User, incident, IncidentOperations.Read);
        if (!canRead.Succeeded)
        {
            return IncidentNotFound();
        }

        var canReadInternal = await authorizationService.AuthorizeAsync(
            httpContext.User, incident, IncidentOperations.ReadInternalComments);

        var details = await queries.GetDetailsAsync(id, canReadInternal.Succeeded, cancellationToken);
        return details is null ? IncidentNotFound() : Results.Ok(details);
    }

    private static async Task<IResult> UpdateAsync(
        Guid id,
        UpdateIncidentRequest request,
        HttpContext httpContext,
        IAuthorizationService authorizationService,
        SecureLabDbContext dbContext,
        CancellationToken cancellationToken)
    {
        var incident = await dbContext.Incidents
            .SingleOrDefaultAsync(item => item.Id == id, cancellationToken); // tracked
        if (incident is null)
        {
            return IncidentNotFound();
        }

        if (!(await authorizationService.AuthorizeAsync(
                httpContext.User, incident, IncidentOperations.Read)).Succeeded)
        {
            return IncidentNotFound();
        }

        if (!(await authorizationService.AuthorizeAsync(
                httpContext.User, incident, IncidentOperations.Update)).Succeeded)
        {
            return Results.Forbid();
        }

        var errors = new Dictionary<string, string[]>();
        if (string.IsNullOrWhiteSpace(request.Title))
        {
            errors["title"] = ["Заголовок обов'язковий."];
        }
        if (string.IsNullOrWhiteSpace(request.Description))
        {
            errors["description"] = ["Опис обов'язковий."];
        }
        if (!Enum.TryParse<IncidentSeverity>(request.Severity, ignoreCase: true, out var severity)
            || !Enum.IsDefined(severity))
        {
            errors["severity"] = ["Некоректне значення severity."];
        }
        if (request.OccurredAtUtc is null)
        {
            errors["occurredAtUtc"] = ["Час події обов'язковий."];
        }
        if (errors.Count > 0)
        {
            return Results.ValidationProblem(errors);
        }

        incident.Title = request.Title.Trim();
        incident.Description = request.Description.Trim();
        incident.Severity = severity;
        incident.OccurredAtUtc = request.OccurredAtUtc!.Value.ToUniversalTime();
        incident.UpdatedAtUtc = DateTimeOffset.UtcNow;
        await dbContext.SaveChangesAsync(cancellationToken);
        return Results.NoContent();
    }

    private static async Task<IResult> DeleteAsync(
        Guid id,
        HttpContext httpContext,
        IAuthorizationService authorizationService,
        SecureLabDbContext dbContext,
        CancellationToken cancellationToken)
    {
        var incident = await dbContext.Incidents
            .SingleOrDefaultAsync(item => item.Id == id, cancellationToken);
        if (incident is null)
        {
            return IncidentNotFound();
        }

        if (!(await authorizationService.AuthorizeAsync(
                httpContext.User, incident, IncidentOperations.Read)).Succeeded)
        {
            return IncidentNotFound();
        }

        if (!(await authorizationService.AuthorizeAsync(
                httpContext.User, incident, IncidentOperations.Delete)).Succeeded)
        {
            return Results.Forbid();
        }

        dbContext.Incidents.Remove(incident);
        await dbContext.SaveChangesAsync(cancellationToken);
        return Results.NoContent();
    }

    private static async Task<IResult> TriageAsync(
        Guid id,
        HttpContext httpContext,
        IAuthorizationService authorizationService,
        SecureLabDbContext dbContext,
        ILogger<Program> logger,
        CancellationToken cancellationToken)
    {
        var incident = await dbContext.Incidents
            .SingleOrDefaultAsync(item => item.Id == id, cancellationToken);
        if (incident is null)
        {
            return IncidentNotFound();
        }

        if (!(await authorizationService.AuthorizeAsync(
                httpContext.User, incident, IncidentOperations.Read)).Succeeded)
        {
            return IncidentNotFound();
        }

        if (!(await authorizationService.AuthorizeAsync(
                httpContext.User, incident, IncidentOperations.Triage)).Succeeded)
        {
            logger.LogWarning(
                "authorization.denied subject={SubjectId} action={Action} "
                + "resource={ResourceId} result={Result} "
                + "occurredAtUtc={OccurredAtUtc:O} traceId={TraceId}",
                httpContext.User.FindFirstValue(ClaimTypes.NameIdentifier),
                "incident.triage",
                incident.Id,
                "denied",
                DateTimeOffset.UtcNow,
                Activity.Current?.Id ?? httpContext.TraceIdentifier);
            return Results.Forbid();
        }

        if (incident.Status != IncidentStatus.New)
        {
            return Results.Conflict();
        }

        incident.Status = IncidentStatus.Triaged;
        incident.UpdatedAtUtc = DateTimeOffset.UtcNow;
        await dbContext.SaveChangesAsync(cancellationToken);
        return Results.NoContent();
    }

    private static async Task<IResult> GetCommentAsync(
        Guid id,
        HttpContext httpContext,
        IAuthorizationService authorizationService,
        SecureLabDbContext dbContext,
        CancellationToken cancellationToken)
    {
        var comment = await dbContext.IncidentComments
            .AsNoTracking()
            .Include(item => item.Incident)
            .Include(item => item.Author)
            .SingleOrDefaultAsync(item => item.Id == id, cancellationToken);
        if (comment is null)
        {
            return IncidentNotFound();
        }

        if (!(await authorizationService.AuthorizeAsync(
                httpContext.User, comment.Incident, IncidentOperations.Read)).Succeeded)
        {
            return IncidentNotFound();
        }

        if (comment.IsInternal
            && !(await authorizationService.AuthorizeAsync(
                httpContext.User, comment.Incident, IncidentOperations.ReadInternalComments)).Succeeded)
        {
            return IncidentNotFound();
        }

        return Results.Ok(new IncidentCommentResponse(
            comment.Id, comment.Author.DisplayName, comment.Text, comment.CreatedAtUtc));
    }
}
