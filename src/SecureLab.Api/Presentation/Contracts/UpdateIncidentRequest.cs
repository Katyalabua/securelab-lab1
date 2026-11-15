namespace SecureLab.Api.Presentation.Contracts;

// Лише дозволені content-поля. owner/role/status клієнт не передає (A-08a: невідомі поля ігноруються).
public sealed record UpdateIncidentRequest(
    string Title,
    string Description,
    string Severity,
    DateTimeOffset? OccurredAtUtc);
