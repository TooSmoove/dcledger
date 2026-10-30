using System.Globalization;
using System.Text.RegularExpressions;
using DcLedger.Application;
using DcLedger.Domain;
using YamlDotNet.RepresentationModel;

namespace DcLedger.Infrastructure.Ledger;

/// <summary>
/// Reads one record's YAML mapping with explicit types. Every value is read as the literal scalar text, so YAML's
/// implicit typing (yes/no booleans, timestamps, octal numbers) never applies. Problems are collected, not thrown.
/// </summary>
internal sealed partial class YamlFields
{
    private static readonly HashSet<string> ForbiddenKeys = new(StringComparer.Ordinal)
    {
        "original_title", "petitioner", "petitioners", "petitioner_name", "applicant", "applicant_name",
        "owner", "owner_name", "property_owner",
    };

    private static readonly string[] UtcFormats = ["yyyy-MM-dd'T'HH:mm:ss'Z'", "yyyy-MM-dd'T'HH:mm:ss.FFFFFFF'Z'"];
    private static readonly string[] OffsetFormats = ["yyyy-MM-dd'T'HH:mm:sszzz", "yyyy-MM-dd'T'HH:mmzzz"];
    private static readonly string[] LocalFormats = ["yyyy-MM-dd'T'HH:mm:ss", "yyyy-MM-dd'T'HH:mm"];

    private readonly Dictionary<string, YamlNode> _values = new(StringComparer.Ordinal);
    private readonly HashSet<string> _read = new(StringComparer.Ordinal);
    private readonly List<ValidationError> _errors;
    private readonly string _file;
    private readonly int _errorsAtStart;

    public YamlFields(YamlMappingNode mapping, string file, List<ValidationError> errors)
    {
        _file = file;
        _errors = errors;
        _errorsAtStart = errors.Count;
        foreach (var (key, value) in mapping.Children)
        {
            if (key is YamlScalarNode { Value: { } name })
            {
                _values[name] = value;
            }
            else
            {
                Add(ValidationRule.ParseError, $"keys must be plain text (line {key.Start.Line}).");
            }
        }
    }

    /// <summary>The record ID once read, so later errors can name it.</summary>
    public string? RecordId { get; set; }

    /// <summary>True when no problem was found in this file.</summary>
    public bool IsValid => _errors.Count == _errorsAtStart;

    public string? Required(string key)
    {
        var value = Optional(key);
        if (string.IsNullOrWhiteSpace(value))
        {
            Add(ValidationRule.RequiredField, $"'{key}' is required.");
            return null;
        }

        return value;
    }

    public string? Optional(string key)
    {
        _read.Add(key);
        if (!_values.TryGetValue(key, out var node))
        {
            return null;
        }

        if (node is YamlScalarNode scalar)
        {
            return scalar.Value;
        }

        Add(ValidationRule.InvalidValue, $"'{key}' must be a single value.");
        return null;
    }

    public IReadOnlyList<string> List(string key)
    {
        _read.Add(key);
        if (!_values.TryGetValue(key, out var node))
        {
            return [];
        }

        if (node is not YamlSequenceNode sequence || sequence.Children.Any(child => child is not YamlScalarNode))
        {
            Add(ValidationRule.InvalidValue, $"'{key}' must be a list of values.");
            return [];
        }

        return [.. sequence.Children.Cast<YamlScalarNode>().Select(child => child.Value ?? string.Empty)];
    }

    public string? Slug(string key)
    {
        var value = Required(key);
        if (value is not null && (value.Length > 100 || !SlugPattern().IsMatch(value)))
        {
            Add(ValidationRule.InvalidValue, $"'{key}' must be lowercase letters, digits and single hyphens.");
            return null;
        }

        return value;
    }

    public TEnum? Choice<TEnum>(string key, IReadOnlyDictionary<string, TEnum> allowed)
        where TEnum : struct, Enum
    {
        var value = Required(key);
        if (value is null)
        {
            return null;
        }

        if (allowed.TryGetValue(value, out var choice))
        {
            return choice;
        }

        Add(ValidationRule.InvalidValue, $"'{key}' must be one of: {string.Join(", ", allowed.Keys)}.");
        return null;
    }

    public Sha256Hash? Hash(string key)
    {
        var value = Required(key);
        return value is null ? null : ToHash(key, value);
    }

    public IReadOnlyList<Sha256Hash> Hashes(string key) =>
        [.. List(key).Select(value => ToHash(key, value)).OfType<Sha256Hash>()];

    public Uri? Url(string key)
    {
        var value = Required(key);
        if (value is null)
        {
            return null;
        }

        if (Uri.TryCreate(value, UriKind.Absolute, out var uri) && (uri.Scheme == Uri.UriSchemeHttps || uri.Scheme == Uri.UriSchemeHttp))
        {
            return uri;
        }

        Add(ValidationRule.InvalidValue, $"'{key}' must be an absolute http or https URL.");
        return null;
    }

    public long? WholeNumber(string key, long max = long.MaxValue)
    {
        var value = Required(key);
        if (value is null)
        {
            return null;
        }

        if (long.TryParse(value, NumberStyles.None, CultureInfo.InvariantCulture, out var count) && count <= max)
        {
            return count;
        }

        Add(ValidationRule.InvalidValue, $"'{key}' must be a whole number of 0 or more.");
        return null;
    }

    public DateTimeOffset? UtcTimestamp(string key, bool required)
    {
        var value = required ? Required(key) : Optional(key);
        if (value is null)
        {
            return null;
        }

        if (DateTimeOffset.TryParseExact(value, UtcFormats, CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal, out var timestamp))
        {
            return timestamp;
        }

        Add(ValidationRule.InvalidValue, $"'{key}' must be a UTC time like 2026-10-25T21:17:05Z.");
        return null;
    }

    /// <summary>A meeting time: must carry an explicit offset; whether it's the right Eastern offset is the validator's job.</summary>
    public DateTimeOffset? OffsetTimestamp(string key)
    {
        var value = Required(key);
        if (value is null)
        {
            return null;
        }

        if (DateTimeOffset.TryParseExact(value, OffsetFormats, CultureInfo.InvariantCulture, DateTimeStyles.None, out var timestamp)
            || DateTimeOffset.TryParseExact(value, UtcFormats, CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal, out timestamp))
        {
            return timestamp;
        }

        if (DateTime.TryParseExact(value, LocalFormats, CultureInfo.InvariantCulture, DateTimeStyles.None, out _))
        {
            Add(ValidationRule.OffsetMissing, $"'{key}' needs a UTC offset, like 2026-11-05T17:30:00-05:00.");
        }
        else
        {
            Add(ValidationRule.InvalidValue, $"'{key}' must be a time like 2026-11-05T17:30:00-05:00.");
        }

        return null;
    }

    public DateOnly? Date(string key)
    {
        var value = Required(key);
        if (value is null)
        {
            return null;
        }

        if (DateOnly.TryParseExact(value, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var date))
        {
            return date;
        }

        Add(ValidationRule.InvalidValue, $"'{key}' must be a date like 2026-11-05.");
        return null;
    }

    /// <summary>
    /// A nested mapping as its own fields, sharing this file's error list and record ID; null when absent, or when it
    /// isn't a mapping (reported).
    /// </summary>
    public YamlFields? Mapping(string key)
    {
        _read.Add(key);
        if (!_values.TryGetValue(key, out var node))
        {
            return null;
        }

        if (node is YamlMappingNode mapping)
        {
            return new YamlFields(mapping, _file, _errors) { RecordId = RecordId };
        }

        Add(ValidationRule.InvalidValue, $"'{key}' must be a mapping.");
        return null;
    }

    /// <summary>
    /// A required decimal number such as <c>36.0726</c> or <c>-79.792</c>: an optional minus sign, 1 to 3 digits, and at
    /// most 6 decimals (about 10 cm). No exponents, thousands separators or plus signs.
    /// </summary>
    public double? Decimal(string key)
    {
        var value = Required(key);
        if (value is null)
        {
            return null;
        }

        if (DecimalPattern().IsMatch(value)
            && double.TryParse(value, NumberStyles.AllowLeadingSign | NumberStyles.AllowDecimalPoint, CultureInfo.InvariantCulture, out var number))
        {
            return number;
        }

        Add(ValidationRule.InvalidValue, $"'{key}' must be a decimal number like 36.0726, with at most 6 decimals.");
        return null;
    }

    /// <summary>Reports every key no reader asked for: forbidden ones by name, the rest as unknown.</summary>
    public void RejectUnreadKeys()
    {
        foreach (var key in _values.Keys.Where(key => !_read.Contains(key)).Order(StringComparer.Ordinal))
        {
            if (ForbiddenKeys.Contains(key))
            {
                Add(ValidationRule.ForbiddenField, $"'{key}' must not be stored: no personal names or original titles in the ledger (SPEC §E).");
            }
            else
            {
                Add(ValidationRule.UnknownField, $"'{key}' isn't part of this record type.");
            }
        }
    }

    private Sha256Hash? ToHash(string key, string value)
    {
        if (Sha256Hash.TryParse(value, out var hash))
        {
            return hash;
        }

        Add(ValidationRule.InvalidValue, $"'{key}' values must be SHA-256 digests: 64 lowercase hex characters.");
        return null;
    }

    private void Add(string rule, string message) => _errors.Add(new ValidationError(_file, RecordId, rule, message));

    [GeneratedRegex("^[a-z0-9]+(?:-[a-z0-9]+)*$", RegexOptions.CultureInvariant, 250)]
    private static partial Regex SlugPattern();

    [GeneratedRegex(@"^-?[0-9]{1,3}(?:\.[0-9]{1,6})?$", RegexOptions.CultureInvariant, 250)]
    private static partial Regex DecimalPattern();
}
