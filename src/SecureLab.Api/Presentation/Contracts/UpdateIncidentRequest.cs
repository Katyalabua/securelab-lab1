namespace SecureLab.Api.Presentation.Contracts;

// VULNERABLE: навмисно містить небезпечні поля для PoC
public sealed record UpdateIncidentRequest(
    string Title,
    string Description,
    string Severity,
    DateTimeOffset? OccurredAtUtc,
    Guid? OwnerUserId,
    string? Status);
