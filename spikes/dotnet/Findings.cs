namespace Spikes;

/// <summary>Appends spike observations to $SPIKE_OUT/findings-&lt;os&gt;.md so CI can publish them.</summary>
internal static class Findings
{
    private static readonly object Gate = new();

    public static void Write(string spike, string text)
    {
        var dir = Environment.GetEnvironmentVariable("SPIKE_OUT");
        if (string.IsNullOrEmpty(dir))
        {
            return;
        }

        Directory.CreateDirectory(dir);
        var os = OperatingSystem.IsWindows() ? "windows" : OperatingSystem.IsLinux() ? "linux" : "other";
        lock (Gate)
        {
            File.AppendAllText(
                Path.Combine(dir, $"findings-{os}.md"),
                $"- **{spike}** ({os}): {text.ReplaceLineEndings(" ⏎ ")}{Environment.NewLine}");
        }
    }
}
