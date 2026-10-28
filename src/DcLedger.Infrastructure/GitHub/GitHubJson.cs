using System.Text.Json.Serialization;

namespace DcLedger.Infrastructure.GitHub;

internal sealed record CreateIssueRequest(
    [property: JsonPropertyName("title")] string Title,
    [property: JsonPropertyName("body")] string Body,
    [property: JsonPropertyName("labels")] IReadOnlyList<string> Labels);

internal sealed record CreateCommentRequest([property: JsonPropertyName("body")] string Body);

internal sealed record UpdateIssueRequest([property: JsonPropertyName("state")] string State);

/// <summary>The two fields read from an issue; GitHub sends many more, which are ignored.</summary>
internal sealed record IssueResponse(
    [property: JsonPropertyName("number")] int Number,
    [property: JsonPropertyName("state")] string State);

internal sealed record CommentResponse([property: JsonPropertyName("id")] long Id);

internal sealed record ErrorResponse([property: JsonPropertyName("message")] string? Message = null);

[JsonSourceGenerationOptions(RespectNullableAnnotations = true, RespectRequiredConstructorParameters = true)]
[JsonSerializable(typeof(CreateIssueRequest))]
[JsonSerializable(typeof(CreateCommentRequest))]
[JsonSerializable(typeof(UpdateIssueRequest))]
[JsonSerializable(typeof(IssueResponse))]
[JsonSerializable(typeof(CommentResponse))]
[JsonSerializable(typeof(ErrorResponse))]
internal sealed partial class GitHubJsonContext : JsonSerializerContext;
