using System.Globalization;
using System.Text;
using DcLedger.Application;
using DcLedger.Domain;
using YamlDotNet.Core;
using YamlDotNet.RepresentationModel;

namespace DcLedger.Infrastructure.Ledger;

/// <summary>
/// Reads <c>data/</c>: one YAML file per record in jurisdictions/, documents/, events/ and actions/ (ADR-0002).
/// Files are capped at 64 KB, must hold exactly one mapping, may not use anchors or aliases, and may only use
/// known keys. Bad files are reported and skipped; this reader never throws for bad data (ADR-0005).
/// </summary>
public sealed class YamlLedgerReader : ILedgerReader
{
    /// <summary>Per-file size cap.</summary>
    public const int MaxFileBytes = 64 * 1024;

    private static readonly string[] Folders = ["jurisdictions", "documents", "events", "actions"];

    // YAML values are the same fixed tokens the exports use (Domain), so the two can't drift apart.
    private static readonly Dictionary<string, Platform> Platforms = ByToken<Platform>(PlatformExtensions.ToToken);
    private static readonly Dictionary<string, SourceType> SourceTypes = ByToken<SourceType>(EnumTokens.ToToken);
    private static readonly Dictionary<string, EventStatus> Statuses = ByToken<EventStatus>(EnumTokens.ToToken);
    private static readonly Dictionary<string, EvidenceLabel> Labels = ByToken<EvidenceLabel>(EnumTokens.ToToken);

    /// <summary>True for paths like <c>events/some-id.yaml</c>: a record file relative to the data directory.</summary>
    public static bool IsRecordPath(string relativePath)
    {
        ArgumentNullException.ThrowIfNull(relativePath);
        var parts = relativePath.Split('/');
        return parts.Length == 2
            && Folders.Contains(parts[0], StringComparer.Ordinal)
            && parts[1].EndsWith(".yaml", StringComparison.Ordinal);
    }

    /// <inheritdoc />
    public LedgerSnapshot Load(string dataDirectory)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(dataDirectory);

        var errors = new List<ValidationError>();
        var files = new List<string>();
        var jurisdictions = new List<LoadedRecord<Jurisdiction>>();
        var documents = new List<LoadedRecord<Document>>();
        var events = new List<LoadedRecord<LedgerEvent>>();
        var actions = new List<LoadedRecord<LocalAction>>();

        foreach (var folder in Folders)
        {
            var directory = Path.Combine(dataDirectory, folder);
            if (!Directory.Exists(directory))
            {
                continue;
            }

            foreach (var path in Directory.EnumerateFiles(directory).Order(StringComparer.Ordinal))
            {
                var relative = $"{folder}/{Path.GetFileName(path)}";
                if (!relative.EndsWith(".yaml", StringComparison.Ordinal))
                {
                    errors.Add(new ValidationError(relative, null, ValidationRule.ParseError, "Only .yaml record files belong in this folder."));
                    continue;
                }

                files.Add(relative);
                var fields = Open(path, relative, errors);
                if (fields is null)
                {
                    continue;
                }

                switch (folder)
                {
                    case "jurisdictions":
                        Keep(jurisdictions, relative, fields, ReadJurisdiction(fields));
                        break;
                    case "documents":
                        Keep(documents, relative, fields, ReadDocument(fields));
                        break;
                    case "events":
                        Keep(events, relative, fields, ReadEvent(fields));
                        break;
                    default:
                        Keep(actions, relative, fields, ReadAction(fields));
                        break;
                }
            }
        }

        return new LedgerSnapshot
        {
            Jurisdictions = jurisdictions,
            Documents = documents,
            Events = events,
            Actions = actions,
            LoadErrors = errors,
            RecordFiles = files,
        };
    }

    private static YamlFields? Open(string path, string relative, List<ValidationError> errors)
    {
        var size = new FileInfo(path).Length;
        if (size > MaxFileBytes)
        {
            errors.Add(new ValidationError(relative, null, ValidationRule.FileTooLarge,
                string.Create(CultureInfo.InvariantCulture, $"{size} bytes; records are capped at {MaxFileBytes} bytes.")));
            return null;
        }

        var stream = new YamlStream();
        try
        {
            using var reader = new StringReader(File.ReadAllText(path, Encoding.UTF8));
            stream.Load(reader);
        }
        catch (YamlException exception)
        {
            errors.Add(new ValidationError(relative, null, ValidationRule.ParseError,
                string.Create(CultureInfo.InvariantCulture, $"YAML error at line {exception.Start.Line}: {exception.Message}")));
            return null;
        }

        if (stream.Documents.Count != 1 || stream.Documents[0].RootNode is not YamlMappingNode mapping)
        {
            errors.Add(new ValidationError(relative, null, ValidationRule.ParseError, "A record file must hold exactly one YAML mapping."));
            return null;
        }

        if (mapping.AllNodes.Any(node => !node.Anchor.IsEmpty))
        {
            errors.Add(new ValidationError(relative, null, ValidationRule.ParseError, "YAML anchors and aliases aren't allowed in record files."));
            return null;
        }

        return new YamlFields(mapping, relative, errors);
    }

    private static void Keep<T>(List<LoadedRecord<T>> records, string relative, YamlFields fields, T? record)
        where T : class
    {
        fields.RejectUnreadKeys();
        if (record is not null && fields.IsValid)
        {
            records.Add(new LoadedRecord<T>(relative, record));
        }
    }

    private static Jurisdiction? ReadJurisdiction(YamlFields f)
    {
        var id = f.RecordId = f.Slug("id");
        var name = f.Required("name");
        var platform = f.Choice("platform", Platforms);
        var client = f.Optional("legistar_client");
        var bodies = f.List("bodies");
        var lastManualCheck = f.UtcTimestamp("last_manual_check_utc", required: false);

        return id is null || name is null || platform is null
            ? null
            : new Jurisdiction
            {
                Id = id,
                Name = name,
                Platform = platform.Value,
                LegistarClient = client,
                Bodies = bodies,
                LastManualCheckUtc = lastManualCheck,
            };
    }

    private static Document? ReadDocument(YamlFields f)
    {
        var id = f.Hash("id");
        f.RecordId = id?.Value;
        var url = f.Url("url");
        var retrievedAt = f.UtcTimestamp("retrieved_at", required: true);
        var mediaType = f.Required("media_type");
        var bytes = f.WholeNumber("bytes");
        var sourceType = f.Choice("source_type", SourceTypes);

        return id is null || url is null || retrievedAt is null || mediaType is null || bytes is null || sourceType is null
            ? null
            : new Document
            {
                Id = id.Value,
                Url = url,
                RetrievedAtUtc = retrievedAt.Value,
                MediaType = mediaType,
                Bytes = bytes.Value,
                SourceType = sourceType.Value,
            };
    }

    private static LedgerEvent? ReadEvent(YamlFields f)
    {
        var id = f.RecordId = f.Slug("id");
        var jurisdictionId = f.Slug("jurisdiction_id");
        var platform = f.Choice("platform", Platforms);
        var meetingId = f.Required("platform_meeting_id");
        var body = f.Required("body");
        var title = f.Required("title");
        var caseNumber = f.Optional("case_number");
        var startsAt = f.OffsetTimestamp("starts_at");
        var status = f.Choice("status", Statuses);
        var sequence = f.WholeNumber("sequence", int.MaxValue);
        var label = f.Choice("label", Labels);
        var documentIds = f.Hashes("document_ids");
        var firstSeen = f.UtcTimestamp("first_seen_utc", required: false);
        var location = ReadLocation(f);

        return id is null || jurisdictionId is null || platform is null || meetingId is null || body is null || title is null
            || startsAt is null || status is null || sequence is null || label is null
            ? null
            : new LedgerEvent
            {
                Id = id,
                JurisdictionId = jurisdictionId,
                Platform = platform.Value,
                PlatformMeetingId = meetingId,
                Body = body,
                Title = title,
                CaseNumber = caseNumber,
                StartsAt = startsAt.Value,
                Status = status.Value,
                Sequence = (int)sequence.Value,
                Label = label.Value,
                DocumentIds = documentIds,
                FirstSeenUtc = firstSeen,
                Location = location,
            };
    }

    /// <summary>
    /// The optional <c>location</c> mapping: exactly <c>lat</c> and <c>lon</c>. Any other key inside it (a name, an
    /// address, an owner) is rejected like an unknown or forbidden top-level key.
    /// </summary>
    private static Location? ReadLocation(YamlFields f)
    {
        var fields = f.Mapping("location");
        if (fields is null)
        {
            return null;
        }

        var latitude = fields.Decimal("lat");
        var longitude = fields.Decimal("lon");
        fields.RejectUnreadKeys();
        return latitude is null || longitude is null ? null : new Location { Latitude = latitude.Value, Longitude = longitude.Value };
    }

    private static LocalAction? ReadAction(YamlFields f)
    {
        var id = f.RecordId = f.Slug("id");
        var jurisdictionId = f.Slug("jurisdiction_id");
        var actionType = f.Required("action_type");
        var outcome = f.Required("outcome");
        var decidedOn = f.Date("decided_on");
        var votes = f.Optional("votes");
        var label = f.Choice("label", Labels);
        var documentIds = f.Hashes("document_ids");

        return id is null || jurisdictionId is null || actionType is null || outcome is null || decidedOn is null || label is null
            ? null
            : new LocalAction
            {
                Id = id,
                JurisdictionId = jurisdictionId,
                ActionType = actionType,
                Outcome = outcome,
                DecidedOn = decidedOn.Value,
                Votes = votes,
                Label = label.Value,
                DocumentIds = documentIds,
            };
    }

    private static Dictionary<string, T> ByToken<T>(Func<T, string> token)
        where T : struct, Enum =>
        Enum.GetValues<T>().ToDictionary(token, StringComparer.Ordinal);
}
