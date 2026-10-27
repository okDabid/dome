using System.Globalization;
using System.Text.RegularExpressions;

namespace Dome.Core;

public record LyricLine(TimeSpan Time, string Text);

public static partial class Lyrics
{
    [GeneratedRegex(@"\[(\d{1,3}):(\d{2})(?:[.:](\d{1,3}))?\]")]
    private static partial Regex Timestamp();
    [GeneratedRegex(@"\[offset:([+-]?\d+)\]", RegexOptions.IgnoreCase)]
    private static partial Regex Offset();

    public static IReadOnlyList<LyricLine> Parse(string? lrc)
    {
        if (string.IsNullOrWhiteSpace(lrc)) return [];
        var offsetMatch = Offset().Match(lrc);
        var offset = offsetMatch.Success && int.TryParse(offsetMatch.Groups[1].Value, out var ms) ? ms : 0;
        var result = new List<LyricLine>();
        foreach (var raw in lrc.Split('\n'))
        {
            var matches = Timestamp().Matches(raw);
            if (matches.Count == 0) continue;
            var text = Timestamp().Replace(raw, "").Trim();
            foreach (Match match in matches)
            {
                var minutes = int.Parse(match.Groups[1].Value, CultureInfo.InvariantCulture);
                var seconds = int.Parse(match.Groups[2].Value, CultureInfo.InvariantCulture);
                if (seconds >= 60) continue;
                var fraction = match.Groups[3].Success
                    ? double.Parse("0." + match.Groups[3].Value, CultureInfo.InvariantCulture) : 0;
                result.Add(new(TimeSpan.FromSeconds(minutes * 60 + seconds + fraction)
                    + TimeSpan.FromMilliseconds(offset), text));
            }
        }
        return result.OrderBy(x => x.Time).ToArray();
    }

    public static int CurrentIndex(IReadOnlyList<LyricLine> lines, TimeSpan position)
    {
        var low = 0;
        var high = lines.Count - 1;
        var found = -1;
        while (low <= high)
        {
            var mid = low + (high - low) / 2;
            if (lines[mid].Time <= position) { found = mid; low = mid + 1; }
            else high = mid - 1;
        }
        return found;
    }
}
