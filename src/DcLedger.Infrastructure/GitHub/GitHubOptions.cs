namespace DcLedger.Infrastructure.GitHub;

/// <summary>
/// Where review issues go. In Actions: <c>GITHUB_REPOSITORY</c> and the job's <c>GITHUB_TOKEN</c> with
/// <c>issues: write</c> (issue #19). A class, not a record, so it never prints the token.
/// </summary>
public sealed class GitHubOptions
{
    /// <summary><c>owner/repo</c>.</summary>
    public string Repository { get; set; } = string.Empty;

    /// <summary>Token with permission to write issues. Never logged.</summary>
    public string Token { get; set; } = string.Empty;

    /// <summary>Labels on every review issue.</summary>
    public IReadOnlyList<string> Labels { get; set; } = ["review"];

    /// <summary>The REST API root; https, or loopback for tests.</summary>
    public Uri ApiBaseUri { get; set; } = new("https://api.github.com/");
}
