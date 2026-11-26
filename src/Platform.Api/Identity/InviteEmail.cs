namespace Platform.Api.Identity;

public static class InviteEmail
{
    public static bool TryNormalize(string input, out string normalized)
    {
        normalized = string.Empty;
        if (string.IsNullOrEmpty(input) || input.Length > 320 || input.Any(c => c > 127 || char.IsControl(c) || char.IsWhiteSpace(c))) return false;
        var parts = input.Split('@');
        if (parts.Length != 2 || parts[0].Length is 0 or > 64 || parts[1].Length is 0 or > 253) return false;
        var local = parts[0];
        if (local[0] == '.' || local[^1] == '.' || local.Contains("..", StringComparison.Ordinal) ||
            local.Any(c => !(char.IsAsciiLetterOrDigit(c) || "!#$%&'*+-/=?^_`{|}~.".Contains(c)))) return false;
        var labels = parts[1].Split('.');
        if (labels.Any(label => label.Length is 0 or > 63 || label[0] == '-' || label[^1] == '-' ||
            label.Any(c => !(char.IsAsciiLetterOrDigit(c) || c == '-')))) return false;
        normalized = local + "@" + parts[1].ToLowerInvariant();
        return true;
    }
}
