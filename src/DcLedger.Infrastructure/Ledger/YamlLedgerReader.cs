using System.Globalization;
using System.Text;
using DcLedger.Application;
using DcLedger.Domain;
using YamlDotNet.Core;
using YamlDotNet.RepresentationModel;

namespace DcLedger.Infrastructure.Ledger;

/// <summary>
/// Reads <c>data/</c>: one YAML file per record in jurisdictions/, documents/, events/, actions/ and corrections/ (ADR-0002).
/// Files are capped at 64 KB, must hold exactly one mapping, may not use anchors or aliases, and may only use
/// known keys. Bad files are reported and skipped; this reader never throws for bad data (ADR-0005).
/// </summary>
public sealed class YamlLedgerReader : ILedgerReader
{
    /// <summary>Per-file size cap.</summary>
    public const int MaxFileBytes = 64 * 1024;

    private static readonly string[] Folders = ["jurisdictions", "documents", "events", "actions", "corrections"];

    /// <summary>First-page pictures, one per document, named by the document's ID (issue #132). Not record files.</summary>
    public const string ThumbnailsFolder = "thumbnails";

    // YAML values are the same fixed tokens the exports use (Domain), so the two can't drift apart.
    private static readonly Dictionary<string, Platform> Platforms = ByToken<Platform>(PlatformExtensions.ToToken);
    private static readonly Dictionary<string, SourceType> SourceTypes = ByToken<SourceType>(EnumTokens.ToToken);
    private static readonly Dictionary<string, Retrieval> Retrievals = ByToken<Retrieval>(EnumTokens.ToToken);
    private static readonly Dictionary<string, DocumentKind> DocumentKinds = ByToken<DocumentKind>(EnumTokens.ToToken);
    private static readonly Dictionary<string, EventStatus> Statuses = ByToken<EventStatus>(EnumTokens.ToToken);
    private static readonly Dictionary<string, EvidenceLabel> Labels = ByToken<EvidenceLabel>(EnumTokens.ToToken);
    private static readonly Dictionary<string, ActionType> ActionTypes = ByToken<ActionType>(EnumTokens.ToToken);
    private static readonly Dictionary<string, ActionOutcome> Outcomes = ByToken<ActionOutcome>(EnumTokens.ToToken);
    private static readonly Dictionary<string, CoverageTier> CoverageTiers = ByToken<CoverageTier>(EnumTokens.ToToken);
    private static readonly Dictionary<string, ActionStatus> ActionStatuses = ByToken<ActionStatus>(EnumTokens.ToToken);

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
        var corrections = new List<LoadedRecord<Correction>>();

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
                    case "actions":
                        Keep(actions, relative, fields, ReadAction(fields));
                        break;
                    default:
                        Keep(corrections, relative, fields, ReadCorrection(fields));
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
            Corrections = corrections,
            Thumbnails = ReadThumbnails(dataDirectory, errors),
            LoadErrors = errors,
            RecordFiles = files,
        };
    }

    /// <summary>
    /// Every <c>thumbnails/{sha256}.webp</c> (issue #132), with its SHA-256. A file with another name or extension, one
    /// over <see cref="Thumbnail.MaxBytes"/>, or one that isn't WebP is reported and skipped; whether each picture may
    /// be published is the validator's call (<see cref="ThumbnailRules"/>).
    /// </summary>
    private static List<ThumbnailFile> ReadThumbnails(string dataDirectory, List<ValidationError> errors)
    {
        var thumbnails = new List<ThumbnailFile>();
        var directory = Path.Combine(dataDirectory, ThumbnailsFolder);
        if (!Directory.Exists(directory))
        {
            return thumbnails;
        }

        foreach (var path in Directory.EnumerateFiles(directory).Order(StringComparer.Ordinal))
        {
            var name = Path.GetFileName(path);
            var relative = $"{ThumbnailsFolder}/{name}";
            if (!name.EndsWith(".webp", StringComparison.Ordinal) || !Sha256Hash.TryParse(name[..^".webp".Length], out var documentId))
            {
                errors.Add(new ValidationError(relative, null, ValidationRule.ThumbnailInvalid,
                    "Only {document sha256}.webp files belong in thumbnails/."));
                continue;
            }

            var size = new FileInfo(path).Length;
            if (size > Thumbnail.MaxBytes)
            {
                errors.Add(new ValidationError(relative, documentId.Value, ValidationRule.FileTooLarge,
                    string.Create(CultureInfo.InvariantCulture, $"{size} bytes; pictures are capped at {Thumbnail.MaxBytes} bytes.")));
                continue;
            }

            var content = File.ReadAllBytes(path);
            if (!IsWebP(content))
            {
                errors.Add(new ValidationError(relative, documentId.Value, ValidationRule.ThumbnailInvalid,
                    "The file isn't a WebP image."));
                continue;
            }

            thumbnails.Add(new ThumbnailFile(relative, documentId, Sha256Hash.Compute(content), content));
        }

        return thumbnails;
    }

    /// <summary>A RIFF container whose form type is WEBP.</summary>
    private static bool IsWebP(ReadOnlySpan<byte> content) =>
        content.Length >= 12 && content[..4].SequenceEqual("RIFF"u8) && content[8..12].SequenceEqual("WEBP"u8);

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
        var coverageTier = f.Choice("coverage_tier", CoverageTiers);
        var note = f.OptionalLine("note", Jurisdiction.MaxNoteLength, "Say what the cited document states; it holds the rest.");
        var noteDocumentIds = f.Hashes("note_document_ids");

        return id is null || name is null || platform is null || coverageTier is null
            ? null
            : new Jurisdiction
            {
                Id = id,
                Name = name,
                Platform = platform.Value,
                LegistarClient = client,
                Bodies = bodies,
                LastManualCheckUtc = lastManualCheck,
                CoverageTier = coverageTier.Value,
                Note = note,
                NoteDocumentIds = noteDocumentIds,
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
        var retrieval = f.OptionalChoice("retrieval", Retrievals, Retrieval.Fetched);
        var kind = f.OptionalChoice("kind", DocumentKinds);
        var thumbnail = ReadThumbnail(f);

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
                Retrieval = retrieval,
                Kind = kind,
                Thumbnail = thumbnail,
            };
    }

    /// <summary>
    /// The optional <c>thumbnail</c> mapping (issue #132): exactly <c>sha256</c>, <c>width</c>, <c>height</c> and
    /// <c>reviewed</c>. <c>reviewed</c> is required, so a pasted block that a person hasn't checked yet says so.
    /// </summary>
    private static Thumbnail? ReadThumbnail(YamlFields f)
    {
        var fields = f.Mapping("thumbnail");
        if (fields is null)
        {
            return null;
        }

        var sha256 = fields.Hash("sha256");
        var width = fields.PositiveNumber("width", Thumbnail.MaxWidth);
        var height = fields.PositiveNumber("height", Thumbnail.MaxHeight);
        var reviewed = fields.TrueOrFalse("reviewed");
        fields.RejectUnreadKeys();
        return sha256 is null || width is null || height is null || reviewed is null
            ? null
            : new Thumbnail { Sha256 = sha256.Value, Width = width.Value, Height = height.Value, Reviewed = reviewed.Value };
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
        var eventId = f.OptionalSlug("event_id");
        var actionType = f.Choice("action_type", ActionTypes);
        var outcome = f.Choice("outcome", Outcomes);
        var decidedOn = f.Date("decided_on");
        var votes = f.Votes("votes");
        var unanimous = f.Unanimous("unanimous", votes);
        var sequence = f.OptionalWholeNumber("sequence", int.MaxValue);
        var label = f.Choice("label", Labels);
        var documentIds = f.Hashes("document_ids");
        var terms = ReadTerms(f);

        return id is null || jurisdictionId is null || actionType is null || outcome is null || decidedOn is null || label is null
            ? null
            : new LocalAction
            {
                Id = id,
                JurisdictionId = jurisdictionId,
                EventId = eventId,
                ActionType = actionType.Value,
                Outcome = outcome.Value,
                DecidedOn = decidedOn.Value,
                Votes = votes,
                Unanimous = unanimous,
                Sequence = (int)(sequence ?? 0),
                Label = label.Value,
                DocumentIds = documentIds,
                Terms = terms,
            };
    }

    /// <summary>The optional terms of a decision (issue #115), flat keys of the action record.</summary>
    private static DecisionTerms ReadTerms(YamlFields f) => new()
    {
        InstrumentNumber = f.TermText("instrument_number"),
        Threshold = f.TermText("threshold"),
        Scope = f.TermText("scope"),
        EffectiveOn = f.OptionalDate("effective_on"),
        ExpiresOn = f.OptionalDate("expires_on"),
        EarlyTermination = f.TermText("early_termination"),
        Exemptions = f.TermText("exemptions"),
        SuccessorId = f.OptionalSlug("successor_id"),
        Status = f.OptionalChoice("status", ActionStatuses),
    };

    /// <summary>One corrections log entry (issue #117).</summary>
    private static Correction? ReadCorrection(YamlFields f)
    {
        var id = f.RecordId = f.Slug("id");
        var reportedOn = f.Date("reported_on");
        var correctedOn = f.Date("corrected_on");
        var recordIds = f.Slugs("record_ids");
        var summary = f.RequiredLine("summary", Correction.MaxSummaryLength);
        var report = f.OptionalWholeNumber("report", int.MaxValue);

        return id is null || reportedOn is null || correctedOn is null || summary is null
            ? null
            : new Correction
            {
                Id = id,
                ReportedOn = reportedOn.Value,
                CorrectedOn = correctedOn.Value,
                RecordIds = recordIds,
                Summary = summary,
                Report = report is null ? null : (int)report.Value,
            };
    }

    private static Dictionary<string, T> ByToken<T>(Func<T, string> token)
        where T : struct, Enum =>
        Enum.GetValues<T>().ToDictionary(token, StringComparer.Ordinal);
}
