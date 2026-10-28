using DcLedger.Application;
using DcLedger.Domain;

namespace DcLedger.Adapters.Legistar.Tests;

/// <summary>
/// Serves tests/fixtures/legistar/{set}/ for Legistar URLs: <c>.../events</c> → events.json,
/// <c>.../events/{id}/eventitems</c> → eventitems-{id}.json, anything else → HTTP 404. Records every request.
/// </summary>
internal sealed class FixtureFetcher(string fixtureSet) : IFetcher
{
    private readonly string _directory = FixturePath(fixtureSet);
    private readonly List<Uri> _requests = [];

    public IReadOnlyList<Uri> Requests => _requests;

    /// <summary>Replaces the answer for one absolute path, for example <c>/v1/guilford/events</c>.</summary>
    public Dictionary<string, Func<Uri, FetchedResource>> Overrides { get; } = new(StringComparer.Ordinal);

    public Task<FetchedResource> GetAsync(Uri uri, CancellationToken cancellationToken)
    {
        _requests.Add(uri);
        if (Overrides.TryGetValue(uri.AbsolutePath, out var respond))
        {
            return Task.FromResult(respond(uri));
        }

        var segments = uri.AbsolutePath.Split('/');
        var file = segments[^1] switch
        {
            "events" => "events.json",
            "eventitems" => $"eventitems-{segments[^2]}.json",
            _ => string.Empty,
        };
        var path = Path.Combine(_directory, file);
        return file.Length > 0 && File.Exists(path)
            ? Task.FromResult(Json(uri, File.ReadAllBytes(path)))
            : throw new FetchFailedException(uri, FetchFailure.HttpStatus, $"{uri} answered HTTP 404.", 404);
    }

    public static FetchedResource Json(Uri uri, byte[] content, string? mediaType = "application/json") =>
        new(content, mediaType, uri, DateTimeOffset.UnixEpoch, Sha256Hash.Compute(content));

    private static string FixturePath(string fixtureSet)
    {
        for (var directory = new DirectoryInfo(AppContext.BaseDirectory); directory is not null; directory = directory.Parent)
        {
            if (File.Exists(Path.Combine(directory.FullName, "DcLedger.slnx")))
            {
                return Path.Combine(directory.FullName, "tests", "fixtures", "legistar", fixtureSet);
            }
        }

        throw new InvalidOperationException("DcLedger.slnx not found above " + AppContext.BaseDirectory);
    }
}
