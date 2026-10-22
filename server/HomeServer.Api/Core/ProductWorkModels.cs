using System.Text.Json;

namespace HomeServer.Core;

public sealed record ProductJob(string Id, string Kind, string UserId, int UserVersion, string? ScopeId, JsonElement Input, JsonElement? Result,
 string Status, int Revision, string? Lease, int Progress, string Message, int Attempts, string DueAt, string CreatedAt, string UpdatedAt);
public sealed record ProductJobCommand(int Revision);
