using Npgsql;
using Platform.Api.Identity;
using Platform.Api.Operations;
using Platform.Infrastructure.Crm;

namespace Platform.Api.Crm;

internal static class ContactAttributeMutation
{
    internal static Task RunAsync(HttpContext ctx, IConfiguration config, string id, string attributeId,
        Func<NpgsqlConnection, NpgsqlTransaction, Guid, Guid, Guid, string, string, CancellationToken, Task<AttributeChange>> mutate) =>
        TenantEndpointTransaction.RunAsync(ctx, config, true, async (connection, tx, tenant, commit) =>
        {
            var ct = ctx.RequestAborted;
            Task Error(int status, string token) => OperationalErrors.WriteProblemAsync(ctx, status, token, ct);
            if (!Guid.TryParseExact(id, "D", out var contact) || !Guid.TryParseExact(attributeId, "D", out var attribute))
            { await Error(400, "REQUEST_INVALID"); return; }
            // A tombstone already visible to this request has the same response as a missing contact.
            // If a DELETE wins after this read, the locked version still reports the write conflict.
            if (await ContactStore.GetAsync(connection, tx, tenant, contact, ct) is null)
            { await Error(404, "NOT_FOUND"); return; }
            var row = await ContactStore.LockByIdAsync(connection, tx, tenant, contact, ct);
            if (row is null) { await Error(404, "NOT_FOUND"); return; }
            var precondition = MembershipPreconditions.Parse(ctx.Request.Headers.IfMatch, out var version);
            if (precondition == MembershipPreconditionResult.Missing) { await Error(428, "PRECONDITION_REQUIRED"); return; }
            if (precondition == MembershipPreconditionResult.Invalid) { await Error(400, "PRECONDITION_INVALID"); return; }
            if (version != row.Contact.Version) { await Error(412, "VERSION_CONFLICT"); return; }
            if (row.Deleted) { await Error(404, "NOT_FOUND"); return; }

            var issuer = ctx.User.FindFirst("iss")!.Value;
            var subject = ctx.User.FindFirst("sub")!.Value;
            var change = await mutate(connection, tx, tenant, contact, attribute, issuer, subject, ct);
            if (change == AttributeChange.Missing) { await Error(404, "NOT_FOUND"); return; }
            if (change == AttributeChange.Invalid) { await Error(400, "REQUEST_INVALID"); return; }
            if (change == AttributeChange.Unrepresentable) { await Error(400, "FIELD_VALUE_UNREPRESENTABLE"); return; }
            var result = row.Contact;
            if (change == AttributeChange.Changed)
            {
                result = await ContactStore.AdvanceVersionAsync(connection, tx, tenant, contact, version, ct);
                if (result is null) { await Error(412, "VERSION_CONFLICT"); return; }
                await ContactMutationJournal.AppendAsync(connection, tx, tenant, contact, issuer, subject, "contact.updated",
                    ctx.TraceIdentifier, result.Version, ct);
            }
            await commit();
            ctx.Response.Headers.ETag = MembershipPreconditions.Format(result.Version);
            await ctx.Response.WriteAsJsonAsync(result, ct);
        }, TenantPermission.ContactsWrite, "CRM_STORE_UNAVAILABLE");
}
