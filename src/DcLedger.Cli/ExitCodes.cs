namespace DcLedger.Cli;

/// <summary>Process exit codes; part of the CLI contract in docs/adr/0004-contracts.md.</summary>
internal static class ExitCodes
{
    /// <summary>The command succeeded.</summary>
    public const int Ok = 0;

    /// <summary>Ledger data failed validation.</summary>
    public const int DataInvalid = 1;

    /// <summary>Bad arguments or missing configuration.</summary>
    public const int Usage = 2;

    /// <summary>An external service (source, R2, GitHub) failed after retries.</summary>
    public const int ExternalFailure = 3;

    /// <summary>An unexpected internal error.</summary>
    public const int Bug = 4;
}
