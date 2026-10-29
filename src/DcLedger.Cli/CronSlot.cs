using System.Globalization;

namespace DcLedger.Cli;

/// <summary>
/// The time a GitHub Actions cron schedule last fired at or before an instant, so a scheduled run can log how late it
/// started (SPEC AC9, spike S5). Covers what the workflows use: minute and hour fields made of numbers, ranges, steps
/// and lists, with <c>*</c> for day, month and weekday. Anything else gives null (no delay logged), never a guess.
/// </summary>
internal static class CronSlot
{
    private const int MaxMinutesBack = 2 * 24 * 60;

    /// <summary>The schedule's latest fire time at or before <paramref name="now"/> (UTC), or null when unsupported.</summary>
    public static DateTimeOffset? Previous(string? schedule, DateTimeOffset now)
    {
        var fields = (schedule ?? string.Empty).Split(' ', StringSplitOptions.RemoveEmptyEntries);
        if (fields.Length != 5 || fields[2] != "*" || fields[3] != "*" || fields[4] != "*")
        {
            return null;
        }

        var minutes = Values(fields[0], 59);
        var hours = Values(fields[1], 23);
        if (minutes is null || hours is null)
        {
            return null;
        }

        var utc = now.ToUniversalTime();
        var slot = new DateTimeOffset(utc.Year, utc.Month, utc.Day, utc.Hour, utc.Minute, 0, TimeSpan.Zero);
        for (var i = 0; i <= MaxMinutesBack; i++, slot = slot.AddMinutes(-1))
        {
            if (hours.Contains(slot.Hour) && minutes.Contains(slot.Minute))
            {
                return slot;
            }
        }

        return null;
    }

    /// <summary>The values a field allows: <c>*</c>, <c>n</c>, <c>a-b</c>, any of those with <c>/step</c>, and lists.</summary>
    private static HashSet<int>? Values(string field, int max)
    {
        var values = new HashSet<int>();
        foreach (var part in field.Split(','))
        {
            var slash = part.Split('/');
            if (slash.Length > 2)
            {
                return null;
            }

            var step = 1;
            if (slash.Length == 2 && (!TryNumber(slash[1], out step) || step < 1))
            {
                return null;
            }

            int first, last;
            if (slash[0] == "*")
            {
                (first, last) = (0, max);
            }
            else if (slash[0].Split('-') is [var low, var high])
            {
                if (!TryNumber(low, out first) || !TryNumber(high, out last))
                {
                    return null;
                }
            }
            else if (TryNumber(slash[0], out first))
            {
                last = slash.Length == 2 ? max : first; // "a/n" means from a to the end, every n
            }
            else
            {
                return null;
            }

            if (first > last || last > max)
            {
                return null;
            }

            for (var value = first; value <= last; value += step)
            {
                values.Add(value);
            }
        }

        return values;
    }

    private static bool TryNumber(string text, out int value) =>
        int.TryParse(text, NumberStyles.None, CultureInfo.InvariantCulture, out value);
}
