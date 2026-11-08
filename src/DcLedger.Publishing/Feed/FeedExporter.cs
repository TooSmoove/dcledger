using DcLedger.Application;

namespace DcLedger.Publishing.Feed;

/// <summary>
/// Writes <c>public/feed.xml</c>, an RSS 2.0 feed with one item per reviewed revision of each event (SPEC AC5, AC4).
/// Links are absolute, so the exporter needs the site's public origin.
/// </summary>
public sealed class FeedExporter : IExporter
{
    private readonly Uri _siteUrl;

    /// <summary>Creates the exporter for the site served at <paramref name="siteUrl"/>, for example <c>https://ncdcledger.org</c>.</summary>
    public FeedExporter(Uri siteUrl)
    {
        ArgumentNullException.ThrowIfNull(siteUrl);
        if (!siteUrl.IsAbsoluteUri || (siteUrl.Scheme != Uri.UriSchemeHttps && siteUrl.Scheme != Uri.UriSchemeHttp))
        {
            throw new ArgumentException("The site URL must be an absolute http(s) URL.", nameof(siteUrl));
        }

        _siteUrl = siteUrl;
    }

    /// <inheritdoc />
    public IReadOnlyList<ExportFile> Export(ExportModel model)
    {
        ArgumentNullException.ThrowIfNull(model);
        return [new ExportFile(ExportPaths.Feed, RssFeedWriter.Write(_siteUrl, model.Events))];
    }
}
