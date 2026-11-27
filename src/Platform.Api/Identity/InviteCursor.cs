namespace Platform.Api.Identity;

internal static class InviteCursor
{
    public static string Encode(Guid tenant, Guid anchor) => MembershipCursor.Encode(tenant, anchor);
    public static bool TryDecode(string value, Guid tenant, out Guid anchor) => MembershipCursor.TryDecode(value, tenant, out anchor);
}
