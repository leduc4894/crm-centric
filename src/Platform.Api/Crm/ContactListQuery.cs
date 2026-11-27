using System.Buffers;
using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;

namespace Platform.Api.Crm;

public sealed record ContactListQuery(int Limit, string? Q, Guid? TagId, DateTimeOffset? UpdatedSince, string? Cursor)
{
    private static readonly Regex TimestampPattern = new(@"\A(?<date>\d{4}-\d{2}-\d{2}[Tt]\d{2}:\d{2}:\d{2})(?:\.(?<fraction>\d+))?(?<zone>[Zz]|[+-]\d{2}:\d{2})\z", RegexOptions.CultureInvariant | RegexOptions.NonBacktracking);

    public static bool TryParse(IQueryCollection query, out ContactListQuery? value)
    {
        value = null;
        int limit = 20;
        string? q = null, cursor = null;
        Guid? tagId = null;
        DateTimeOffset? updatedSince = null;
        foreach (var pair in query)
        {
            if (pair.Value.Count != 1 || pair.Value[0] is not string input) return false;
            switch (pair.Key)
            {
                case "limit":
                    if (!int.TryParse(input, NumberStyles.None, CultureInfo.InvariantCulture, out limit) || limit is < 1 or > 100 || input != limit.ToString(CultureInfo.InvariantCulture)) return false;
                    break;
                case "q":
                    if (!ValidSearch(input)) return false;
                    q = input;
                    break;
                case "cursor":
                    if (input.Length is 0 or > 2048) return false;
                    cursor = input;
                    break;
                case "tagId":
                    if (!Guid.TryParseExact(input, "D", out var tag) || input != tag.ToString("D")) return false;
                    tagId = tag;
                    break;
                case "updatedSince":
                    if (!TryTimestamp(input, out var timestamp)) return false;
                    updatedSince = timestamp;
                    break;
                default: return false;
            }
        }
        value = new(limit, q, tagId, updatedSince, cursor);
        return true;
    }

    private static bool ValidSearch(string input)
    {
        if (input.Length == 0) return false;
        var remaining = input.AsSpan();
        var count = 0;
        while (!remaining.IsEmpty)
        {
            if (Rune.DecodeFromUtf16(remaining, out var rune, out var consumed) != OperationStatus.Done || rune.Value == 0 || ++count > 200) return false;
            remaining = remaining[consumed..];
        }
        return true;
    }

    private static bool TryTimestamp(string input, out DateTimeOffset result)
    {
        result = default;
        var match = TimestampPattern.Match(input);
        if (!match.Success) return false;
        var fraction = match.Groups["fraction"].Value;
        var micros = fraction.PadRight(6, '0')[..6];
        var roundUp = fraction.Length > 6 && fraction.AsSpan(6).ContainsAnyExcept('0');
        var zone = match.Groups["zone"].Value;
        if (zone.Equals("z", StringComparison.OrdinalIgnoreCase)) zone = "+00:00";
        var normalized = match.Groups["date"].Value.Replace('t', 'T') + "." + micros + zone;
        if (!DateTimeOffset.TryParseExact(normalized, "yyyy-MM-dd'T'HH:mm:ss.ffffffzzz", CultureInfo.InvariantCulture, DateTimeStyles.None, out var parsed)) return false;
        try { result = parsed.ToUniversalTime().AddTicks(roundUp ? 10 : 0); return true; }
        catch (ArgumentOutOfRangeException) { return false; }
    }
}
