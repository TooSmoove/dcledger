using System.Text.Json.Serialization;

namespace DcLedger.Adapters.Legistar;

/// <summary>
/// The fields of a Legistar event the adapter reads (names as in the Web API; tests/fixtures/legistar has recorded
/// examples). Legistar sends many more, which are ignored. Parameters without a default are required.
/// </summary>
internal sealed record LegistarEvent(
    [property: JsonPropertyName("EventId")] int EventId,
    [property: JsonPropertyName("EventBodyName")] string EventBodyName,
    [property: JsonPropertyName("EventDate")] DateTime EventDate,
    [property: JsonPropertyName("EventTime")] string? EventTime = null,
    [property: JsonPropertyName("EventAgendaFile")] string? EventAgendaFile = null,
    [property: JsonPropertyName("EventInSiteURL")] string? EventInSiteUrl = null,
    [property: JsonPropertyName("EventComment")] string? EventComment = null);

/// <summary>The fields of a Legistar event item the adapter reads.</summary>
internal sealed record LegistarEventItem(
    [property: JsonPropertyName("EventItemId")] int EventItemId,
    [property: JsonPropertyName("EventItemAgendaSequence")] int? EventItemAgendaSequence = null,
    [property: JsonPropertyName("EventItemAgendaNumber")] string? EventItemAgendaNumber = null,
    [property: JsonPropertyName("EventItemTitle")] string? EventItemTitle = null,
    [property: JsonPropertyName("EventItemMatterName")] string? EventItemMatterName = null,
    [property: JsonPropertyName("EventItemMatterFile")] string? EventItemMatterFile = null,
    [property: JsonPropertyName("EventItemMatterType")] string? EventItemMatterType = null);

/// <summary>Source-generated, statically typed parsing (ADR-0003): no reflection over untrusted JSON.</summary>
[JsonSourceGenerationOptions(RespectNullableAnnotations = true, RespectRequiredConstructorParameters = true)]
[JsonSerializable(typeof(LegistarEvent[]))]
[JsonSerializable(typeof(LegistarEventItem[]))]
internal sealed partial class LegistarJsonContext : JsonSerializerContext;
