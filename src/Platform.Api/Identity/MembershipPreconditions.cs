using System.Globalization;
using Microsoft.Extensions.Primitives;

namespace Platform.Api.Identity;

public enum MembershipPreconditionResult { Missing, Invalid, Valid }

public static class MembershipPreconditions
{
    public static string Format(long version)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(version);
        return $"\"v{version.ToString(CultureInfo.InvariantCulture)}\"";
    }

    public static MembershipPreconditionResult Parse(StringValues values, out long version)
    {
        version = default;
        if (values.Count == 0) return MembershipPreconditionResult.Missing;
        if (values.Count != 1) return MembershipPreconditionResult.Invalid;
        var value = values[0];
        if (value is null || value.Length < 4 || value[0] != '"' || value[1] != 'v' || value[^1] != '"')
            return MembershipPreconditionResult.Invalid;
        var digits = value.AsSpan(2, value.Length - 3);
        if (digits.Length == 0 || digits[0] == '0' ||
            !long.TryParse(digits, NumberStyles.None, CultureInfo.InvariantCulture, out var parsed) || parsed <= 0)
            return MembershipPreconditionResult.Invalid;
        version = parsed;
        return MembershipPreconditionResult.Valid;
    }
}
