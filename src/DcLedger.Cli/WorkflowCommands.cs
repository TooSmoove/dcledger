namespace DcLedger.Cli;

/// <summary>GitHub Actions workflow commands, escaped as the toolkit does (https://github.com/actions/toolkit, command.ts).</summary>
internal static class WorkflowCommands
{
    /// <summary><c>::error title=…::message</c>; untrusted text can't start a second command.</summary>
    public static string Error(string title, string message) => $"::error title={EscapeProperty(title)}::{EscapeData(message)}";

    /// <summary><c>::error file=…,title=…::message</c>.</summary>
    public static string FileError(string file, string title, string message) =>
        $"::error file={EscapeProperty(file)},title={EscapeProperty(title)}::{EscapeData(message)}";

    /// <summary><c>::warning title=…::message</c>.</summary>
    public static string Warning(string title, string message) => $"::warning title={EscapeProperty(title)}::{EscapeData(message)}";

    private static string EscapeData(string value) =>
        value.Replace("%", "%25", StringComparison.Ordinal).Replace("\r", "%0D", StringComparison.Ordinal).Replace("\n", "%0A", StringComparison.Ordinal);

    private static string EscapeProperty(string value) =>
        EscapeData(value).Replace(":", "%3A", StringComparison.Ordinal).Replace(",", "%2C", StringComparison.Ordinal);
}
