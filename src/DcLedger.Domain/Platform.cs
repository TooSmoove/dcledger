namespace DcLedger.Domain;

/// <summary>Where a jurisdiction's meetings come from.</summary>
public enum Platform
{
    /// <summary>Granicus Legistar Web API (Guilford County, Winston-Salem).</summary>
    Legistar,

    /// <summary>Entered by a reviewer from the official site (Greensboro, on eSCRIBE, in v0.1).</summary>
    Manual,
}

/// <summary>Fixed lowercase tokens for each platform, used in event UIDs and storage keys. They must never change.</summary>
public static class PlatformExtensions
{
    /// <summary>Returns <c>legistar</c> or <c>manual</c>. Renaming the enum can't change these.</summary>
    public static string ToToken(this Platform platform) => platform switch
    {
        Platform.Legistar => "legistar",
        Platform.Manual => "manual",
        _ => throw new ArgumentOutOfRangeException(nameof(platform), platform, "Unknown platform."),
    };
}
