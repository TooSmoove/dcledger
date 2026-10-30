namespace DcLedger.Domain;

/// <summary>
/// Where a proposed project is, for its marker on the map (issue #22): WGS 84 decimal degrees, as the reviewer read it
/// from the official record. Only a point; no name or address is stored, so the ledger can't become a list of property
/// owners (SPEC privacy). Optional on every event; items without one simply have no marker.
/// </summary>
public sealed record Location
{
    /// <summary>South edge of North Carolina's bounding box, the one the basemap is cut to (basemap.yml NC_BBOX).</summary>
    public const double MinLatitude = 33.84;

    /// <summary>North edge; see <see cref="MinLatitude"/>.</summary>
    public const double MaxLatitude = 36.59;

    /// <summary>West edge; see <see cref="MinLatitude"/>.</summary>
    public const double MinLongitude = -84.33;

    /// <summary>East edge; see <see cref="MinLatitude"/>.</summary>
    public const double MaxLongitude = -75.46;

    /// <summary>Latitude in decimal degrees.</summary>
    public required double Latitude { get; init; }

    /// <summary>Longitude in decimal degrees.</summary>
    public required double Longitude { get; init; }

    /// <summary>True inside North Carolina's bounding box, which is as far as the basemap goes.</summary>
    public bool IsInNorthCarolina =>
        Latitude is >= MinLatitude and <= MaxLatitude && Longitude is >= MinLongitude and <= MaxLongitude;
}
