namespace DcLedger.Domain;

/// <summary>Where a jurisdiction's meetings come from.</summary>
public enum Platform
{
    /// <summary>Granicus Legistar Web API (Guilford County, Winston-Salem).</summary>
    Legistar,

    /// <summary>Entered by a reviewer from the official site (Greensboro, on eSCRIBE, in v0.1).</summary>
    Manual,
}
