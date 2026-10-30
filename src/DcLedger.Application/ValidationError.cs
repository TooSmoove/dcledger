namespace DcLedger.Application;

/// <summary>One problem with ledger data. <paramref name="File"/> is relative to the data directory, with '/' separators.</summary>
public sealed record ValidationError(string File, string? RecordId, string Rule, string Message);

/// <summary>Rule codes reported by the reader (shape) and the validator (meaning). Stable: CI output and docs cite them.</summary>
public static class ValidationRule
{
    /// <summary>The file isn't one YAML mapping, or uses anchors or aliases.</summary>
    public const string ParseError = "PARSE_ERROR";

    /// <summary>The file is over the per-record size cap.</summary>
    public const string FileTooLarge = "FILE_TOO_LARGE";

    /// <summary>A key the schema doesn't define.</summary>
    public const string UnknownField = "UNKNOWN_FIELD";

    /// <summary>A key that would store personal information or an original source title.</summary>
    public const string ForbiddenField = "FORBIDDEN_FIELD";

    /// <summary>A required key is missing or blank.</summary>
    public const string RequiredField = "REQUIRED_FIELD";

    /// <summary>A value has the wrong format or isn't one of the allowed values.</summary>
    public const string InvalidValue = "INVALID_VALUE";

    /// <summary>A meeting time has no UTC offset.</summary>
    public const string OffsetMissing = "OFFSET_MISSING";

    /// <summary>A meeting time's offset isn't the Eastern offset in effect at that time.</summary>
    public const string OffsetNotEastern = "OFFSET_NOT_EASTERN";

    /// <summary>A meeting time is skipped or repeated by a daylight-saving change.</summary>
    public const string DstGapOrOverlap = "DST_GAP_OR_OVERLAP";

    /// <summary>A record's ID doesn't match its file name.</summary>
    public const string IdMismatch = "ID_MISMATCH";

    /// <summary>An item has no document, or references one that is missing or failed to load.</summary>
    public const string DocMissing = "DOC_MISSING";

    /// <summary>An item references a jurisdiction that is missing or failed to load.</summary>
    public const string FkMissing = "FK_MISSING";

    /// <summary>"confirmed_official" without a document from an official source (SPEC AC6).</summary>
    public const string LabelInvalid = "LABEL_INVALID";

    /// <summary>A record that existed before was removed or renamed; published IDs are permanent.</summary>
    public const string SlugChanged = "SLUG_CHANGED";

    /// <summary>A location outside North Carolina's bounding box, which is as far as the basemap goes.</summary>
    public const string LocationOutOfRange = "LOCATION_OUT_OF_RANGE";
}
