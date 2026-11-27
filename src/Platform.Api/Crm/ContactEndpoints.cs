using System.Buffers;
using System.Net.Mail;
using Npgsql;
using System.Text;
using System.Text.Json;
using Platform.Api.Identity;
using Platform.Api.Operations;
using Platform.Infrastructure.Crm;

namespace Platform.Api.Crm;

public static class ContactEndpoints
{
    public static IEndpointRouteBuilder MapContactEndpoints(this IEndpointRouteBuilder endpoints)
    {
        endpoints.MapGet("/api/v1/contacts",ListAsync).RequireAuthorization().WithMetadata(new RequireAccessToken());
        endpoints.MapPost("/api/v1/contacts",CreateAsync).RequireAuthorization().WithMetadata(new RequireAccessToken());
        endpoints.MapGet("/api/v1/contacts/{id}",GetAsync).RequireAuthorization().WithMetadata(new RequireAccessToken());
        endpoints.MapPatch("/api/v1/contacts/{id}",PatchAsync).RequireAuthorization().WithMetadata(new RequireAccessToken());
        endpoints.MapDelete("/api/v1/contacts/{id}",DeleteAsync).RequireAuthorization().WithMetadata(new RequireAccessToken());
        return endpoints;
    }

    private static Task Error(HttpContext context,int status,string reason) => OperationalErrors.WriteProblemAsync(context,status,reason,context.RequestAborted);

    private static async Task ListAsync(HttpContext context, IConfiguration configuration)
    {
        await TenantEndpointTransaction.RunAsync(context,configuration,false,async (connection,transaction,tenant,commit) =>
        {
            if (context.Request.Query.TryGetValue("cursor", out var suppliedCursor) && suppliedCursor.Count == 1 && suppliedCursor[0] is { Length: 0 or > 2048 })
            { await Error(context,400,"CURSOR_INVALID"); return; }
            if (!ValidQueryEncoding(context.Request.QueryString.Value) || !ContactListQuery.TryParse(context.Request.Query,out var query))
            { await Error(context,400,"REQUEST_INVALID"); return; }
            if (!ContactCursor.TryCreate(configuration,out var signer)) { await Error(context,503,"CRM_CURSOR_UNAVAILABLE"); return; }
            DateTime? anchor=null; Guid? anchorId=null;
            if (query!.Cursor is not null)
            {
                if (!signer!.TryDecode(query.Cursor,tenant,query,out var time,out var id)) { await Error(context,400,"CURSOR_INVALID"); return; }
                anchor=time; anchorId=id;
            }
            var rows=await ContactStore.ListAsync(connection,transaction,tenant,query.Q,query.TagId,query.UpdatedSince,anchor,anchorId,query.Limit,context.RequestAborted);
            string? next=null;
            if (rows.Count>query.Limit) { rows.RemoveAt(rows.Count-1); var last=rows[^1]; next=signer!.Encode(tenant,query,last.UpdatedAt,last.Id); }
            await commit(); await context.Response.WriteAsJsonAsync(new { items=rows,nextCursor=next },cancellationToken:context.RequestAborted);
        },TenantPermission.ContactsRead,"CRM_STORE_UNAVAILABLE");
    }

    // Validate percent-decoded bytes before ASP.NET replaces malformed UTF-8.
    private static bool ValidQueryEncoding(string? raw)
    {
        if (raw is null) return true;
        try
        {
            var utf8=new UTF8Encoding(false,true);
            foreach (var part in raw.TrimStart('?').Split('&','='))
            {
                using var bytes=new MemoryStream();
                for (int i=0;i<part.Length;i++)
                {
                    if (part[i]=='%')
                    {
                        if (i+2>=part.Length || !byte.TryParse(part.AsSpan(i+1,2),System.Globalization.NumberStyles.HexNumber,System.Globalization.CultureInfo.InvariantCulture,out var b)) return false;
                        bytes.WriteByte(b); i+=2;
                    }
                    else
                    {
                        var start=i;
                        while (i+1<part.Length && part[i+1]!='%') i++;
                        bytes.Write(utf8.GetBytes(part[start..(i+1)]));
                    }
                }
                _=utf8.GetString(bytes.ToArray());
            }
            return true;
        }
        catch (DecoderFallbackException) { return false; }
        catch (EncoderFallbackException) { return false; }
    }

    private static async Task GetAsync(HttpContext context, IConfiguration configuration, string id)
    {
        await TenantEndpointTransaction.RunAsync(context,configuration,false,async (connection,transaction,tenant,commit) =>
        {
            if (!Guid.TryParseExact(id,"D",out var contactId)) { await Error(context,400,"REQUEST_INVALID"); return; }
            var contact=await ContactStore.GetAsync(connection,transaction,tenant,contactId,context.RequestAborted);
            if (contact is null) { await Error(context,404,"NOT_FOUND"); return; }
            await commit();
            context.Response.Headers.ETag=MembershipPreconditions.Format(contact.Version);
            await context.Response.WriteAsJsonAsync(contact,cancellationToken:context.RequestAborted);
        },TenantPermission.ContactsRead,"CRM_STORE_UNAVAILABLE");
    }

    private static async Task CreateAsync(HttpContext context,IConfiguration configuration)
    {
        var cached = await ReadAuthorizedBodyAsync(context,configuration);
        if (cached is null) return;
        await TenantEndpointTransaction.RunAsync(context,configuration,true,async (connection,transaction,tenant,commit) =>
        {
            string? name=null,email=null,phone=null;
            var phonePresent=false;
            try
            {
                using var doc=JsonDocument.Parse(cached);
                if (doc.RootElement.ValueKind != JsonValueKind.Object) throw new JsonException();
                var seen=new HashSet<string>(StringComparer.Ordinal);
                foreach (var property in doc.RootElement.EnumerateObject())
                {
                    var key=property.Name;
                    if (!seen.Add(key) || (key != "displayName" && key != "email" && key != "phone")) throw new JsonException();
                    if (property.Value.ValueKind is not (JsonValueKind.String or JsonValueKind.Null)) throw new JsonException();
                    var value=property.Value.GetString();
                    if (value is not null && (!ValidText(value) || value.EnumerateRunes().Count() > (key == "displayName" ? 200 : key == "email" ? 320 : 40))) throw new JsonException();
                    if (key == "displayName") { if (string.IsNullOrEmpty(value)) throw new JsonException(); name=value; }
                    else if (key == "email") { if (value is not null) { value=value.Trim(' '); if (!ValidEmail(value)) throw new JsonException(); } email=value; }
                    else { phone=value; phonePresent=true; }
                }
                if (name is null) throw new JsonException();
            }
            catch (Exception e) when (e is JsonException or InvalidOperationException or FormatException) { await Error(context,400,"REQUEST_INVALID"); return; }
            var precondition=MembershipPreconditions.Parse(context.Request.Headers.IfMatch,out var version);
            if (precondition == MembershipPreconditionResult.Invalid) { await Error(context,400,"PRECONDITION_INVALID"); return; }
            var hasExpectedId=context.Request.Headers.TryGetValue("X-Contact-Id",out var expectedValues);
            Guid expectedId=default;
            if (hasExpectedId && (expectedValues.Count != 1 || !Guid.TryParseExact(expectedValues[0],"D",out expectedId)
                || !string.Equals(expectedValues[0],expectedId.ToString("D"),StringComparison.Ordinal)))
            { await Error(context,400,"PRECONDITION_INVALID"); return; }
            if ((precondition == MembershipPreconditionResult.Valid) != hasExpectedId)
            { await Error(context,428,"PRECONDITION_REQUIRED"); return; }
            ContactRecord contact;
            string action;
            if (email is not null)
            {
                var current=await ContactStore.LockByEmailAsync(connection,transaction,tenant,email,context.RequestAborted);
                if (current is not null)
                {
                    if (precondition == MembershipPreconditionResult.Missing) { await Error(context,428,"PRECONDITION_REQUIRED"); return; }
                    if (expectedId != current.Id || version != current.Version) { await Error(context,412,"VERSION_CONFLICT"); return; }
                    if (name == current.DisplayName && email == current.Email && (!phonePresent || phone == current.Phone))
                    {
                        await commit(); context.Response.Headers.ETag=MembershipPreconditions.Format(current.Version);
                        await context.Response.WriteAsJsonAsync(current,cancellationToken:context.RequestAborted); return;
                    }
                    contact=(await ContactStore.UpdateAsync(connection,transaction,tenant,current.Id,version,name,email,phonePresent,phone,context.RequestAborted))!;
                    if (contact is null) { await Error(context,412,"VERSION_CONFLICT"); return; }
                    action="contact.updated";
                }
                else
                {
                    if (precondition == MembershipPreconditionResult.Valid) { await Error(context,412,"VERSION_CONFLICT"); return; }
                    // A PATCH may claim this email after the lookup but before
                    // INSERT. Roll back only that statement and re-evaluate the
                    // active match instead of treating the race as a generic 409.
                    ContactRecord? created=null;
                    for (var attempt=0; attempt<3 && created is null; attempt++)
                    {
                        const string savepoint="contact_email_insert";
                        await transaction.SaveAsync(savepoint,context.RequestAborted);
                        try
                        {
                            created=await ContactStore.CreateAsync(connection,transaction,tenant,name,email,phone,context.RequestAborted);
                            await transaction.ReleaseAsync(savepoint,context.RequestAborted);
                        }
                        catch (PostgresException ex) when (ex.SqlState == PostgresErrorCodes.UniqueViolation && ex.ConstraintName == "contacts_active_email_unique")
                        {
                            await transaction.RollbackAsync(savepoint,context.RequestAborted);
                            await transaction.ReleaseAsync(savepoint,context.RequestAborted);
                            if (await ContactStore.LockByEmailAsync(connection,transaction,tenant,email,context.RequestAborted) is not null)
                            { await Error(context,428,"PRECONDITION_REQUIRED"); return; }
                        }
                        catch (PostgresException ex) when (ex.SqlState == PostgresErrorCodes.UniqueViolation)
                        { await Error(context,409,"CONTACT_EMAIL_CONFLICT"); return; }
                    }
                    if (created is null) throw new InvalidDataException("Contact email contention could not be resolved.");
                    contact=created;
                    action="contact.created";
                }
            }
            else
            {
                if (precondition == MembershipPreconditionResult.Valid) { await Error(context,412,"VERSION_CONFLICT"); return; }
                contact=await ContactStore.CreateAsync(connection,transaction,tenant,name,null,phone,context.RequestAborted);
                action="contact.created";
            }
            await ContactMutationJournal.AppendAsync(connection,transaction,tenant,contact.Id,context.User.FindFirst("iss")!.Value,
                context.User.FindFirst("sub")!.Value,action,context.TraceIdentifier,contact.Version,context.RequestAborted);
            await commit();
            context.Response.StatusCode=action == "contact.created" ? 201 : 200;
            context.Response.Headers.ETag=MembershipPreconditions.Format(contact.Version);
            if (action == "contact.created") context.Response.Headers.Location=$"/api/v1/contacts/{contact.Id}";
            await context.Response.WriteAsJsonAsync(contact,cancellationToken:context.RequestAborted);
        },TenantPermission.ContactsWrite,"CRM_STORE_UNAVAILABLE");
    }

    private static async Task PatchAsync(HttpContext context,IConfiguration configuration,string id)
    {
        var cached=await ReadAuthorizedBodyAsync(context,configuration);
        if (cached is null) return;
        await TenantEndpointTransaction.RunAsync(context,configuration,true,async (connection,transaction,tenant,commit) =>
        {
            if (!Guid.TryParseExact(id,"D",out var contactId)) { await Error(context,400,"REQUEST_INVALID"); return; }
            var row=await ContactStore.LockByIdAsync(connection,transaction,tenant,contactId,context.RequestAborted);
            if (row is null) { await Error(context,404,"NOT_FOUND"); return; }
            var precondition=MembershipPreconditions.Parse(context.Request.Headers.IfMatch,out var version);
            if (precondition == MembershipPreconditionResult.Missing) { await Error(context,428,"PRECONDITION_REQUIRED"); return; }
            if (precondition == MembershipPreconditionResult.Invalid) { await Error(context,400,"PRECONDITION_INVALID"); return; }
            if (version != row.Contact.Version) { await Error(context,412,"VERSION_CONFLICT"); return; }
            if (row.Deleted) { await Error(context,404,"NOT_FOUND"); return; }
            string? name=null,email=null,phone=null; bool namePresent=false,emailPresent=false,phonePresent=false;
            try
            {
                using var doc=JsonDocument.Parse(cached);
                if (doc.RootElement.ValueKind != JsonValueKind.Object) throw new JsonException();
                var seen=new HashSet<string>(StringComparer.Ordinal);
                foreach (var property in doc.RootElement.EnumerateObject())
                {
                    var key=property.Name;
                    if (!seen.Add(key) || (key != "displayName" && key != "email" && key != "phone")) throw new JsonException();
                    if (property.Value.ValueKind is not (JsonValueKind.String or JsonValueKind.Null)) throw new JsonException();
                    var value=property.Value.GetString();
                    if (value is not null && (!ValidText(value) || value.EnumerateRunes().Count() > (key == "displayName" ? 200 : key == "email" ? 320 : 40))) throw new JsonException();
                    if (key == "displayName") { if (string.IsNullOrEmpty(value)) throw new JsonException(); name=value; namePresent=true; }
                    else if (key == "email") { if (value is not null) { value=value.Trim(' '); if (!ValidEmail(value)) throw new JsonException(); } email=value; emailPresent=true; }
                    else { phone=value; phonePresent=true; }
                }
                if (seen.Count == 0) throw new JsonException();
            }
            catch (Exception e) when (e is JsonException or InvalidOperationException or FormatException) { await Error(context,400,"REQUEST_INVALID"); return; }
            var current=row.Contact;
            if ((!namePresent || name==current.DisplayName) && (!emailPresent || email==current.Email) && (!phonePresent || phone==current.Phone))
            {
                await commit(); context.Response.Headers.ETag=MembershipPreconditions.Format(current.Version);
                await context.Response.WriteAsJsonAsync(current,cancellationToken:context.RequestAborted); return;
            }
            ContactRecord? updated;
            try { updated=await ContactStore.PatchAsync(connection,transaction,tenant,contactId,version,namePresent,name,emailPresent,email,phonePresent,phone,context.RequestAborted); }
            catch (PostgresException ex) when (ex.SqlState == PostgresErrorCodes.UniqueViolation) { await Error(context,409,"CONTACT_EMAIL_CONFLICT"); return; }
            if (updated is null) { await Error(context,412,"VERSION_CONFLICT"); return; }
            await ContactMutationJournal.AppendAsync(connection,transaction,tenant,contactId,context.User.FindFirst("iss")!.Value,context.User.FindFirst("sub")!.Value,"contact.updated",context.TraceIdentifier,updated.Version,context.RequestAborted);
            await commit(); context.Response.Headers.ETag=MembershipPreconditions.Format(updated.Version);
            await context.Response.WriteAsJsonAsync(updated,cancellationToken:context.RequestAborted);
        },TenantPermission.ContactsWrite,"CRM_STORE_UNAVAILABLE");
    }

    private static async Task DeleteAsync(HttpContext context,IConfiguration configuration,string id)
    {
        await TenantEndpointTransaction.RunAsync(context,configuration,true,async (connection,transaction,tenant,commit) =>
        {
            if (!Guid.TryParseExact(id,"D",out var contactId)) { await Error(context,400,"REQUEST_INVALID"); return; }
            var row=await ContactStore.LockByIdAsync(connection,transaction,tenant,contactId,context.RequestAborted);
            if (row is null) { await Error(context,404,"NOT_FOUND"); return; }
            var precondition=MembershipPreconditions.Parse(context.Request.Headers.IfMatch,out var version);
            if (precondition == MembershipPreconditionResult.Missing) { await Error(context,428,"PRECONDITION_REQUIRED"); return; }
            if (precondition == MembershipPreconditionResult.Invalid) { await Error(context,400,"PRECONDITION_INVALID"); return; }
            if (version != row.Contact.Version) { await Error(context,412,"VERSION_CONFLICT"); return; }
            if (row.Deleted) { await Error(context,404,"NOT_FOUND"); return; }
            var updatedVersion=await ContactStore.DeleteAsync(connection,transaction,tenant,contactId,version,context.RequestAborted);
            if (updatedVersion is null) { await Error(context,412,"VERSION_CONFLICT"); return; }
            await ContactMutationJournal.AppendAsync(connection,transaction,tenant,contactId,context.User.FindFirst("iss")!.Value,context.User.FindFirst("sub")!.Value,"contact.deleted",context.TraceIdentifier,updatedVersion.Value,context.RequestAborted);
            await commit(); context.Response.StatusCode=204;
        },TenantPermission.ContactsWrite,"CRM_STORE_UNAVAILABLE");
    }

    // Authorization is checked before reading a potentially slow request body,
    // then repeated inside the mutation transaction to fence revocation races.
    internal static async Task<bool> AuthorizeJsonBodyAsync(HttpContext context, IConfiguration configuration, TenantPermission permission)
    {
        var authorized = false;
        await TenantEndpointTransaction.RunAsync(context, configuration, false, async (_, _, _, commit) =>
        {
            await commit();
            authorized = true;
        }, permission, "CRM_STORE_UNAVAILABLE");
        if (!authorized) return false;
        if (context.Request.HasJsonContentType()) return true;
        await Error(context, 415, "REQUEST_INVALID");
        return false;
    }

    internal static async Task<byte[]?> ReadAuthorizedBodyAsync(HttpContext context,IConfiguration configuration, TenantPermission permission = TenantPermission.ContactsWrite)
    {
        if (!await AuthorizeJsonBodyAsync(context, configuration, permission)) return null;
        const int maxBytes=8192; using var buffer=new MemoryStream(); var chunk=new byte[1024]; int count;
        while ((count=await context.Request.Body.ReadAsync(chunk,context.RequestAborted)) != 0)
        {
            if (buffer.Length+count>maxBytes) { await Error(context,413,"REQUEST_INVALID"); return null; }
            buffer.Write(chunk,0,count);
        }
        return buffer.ToArray();
    }

    internal static bool ValidText(string value)
    {
        var remaining=value.AsSpan();
        while (!remaining.IsEmpty)
        {
            if (Rune.DecodeFromUtf16(remaining,out var rune,out var consumed) != OperationStatus.Done || rune.Value == 0) return false;
            remaining=remaining[consumed..];
        }
        return true;
    }
    private static bool ValidEmail(string value)
    {
        if (value.Any(char.IsWhiteSpace) || value.Count(c=>c=='@') != 1) return false;
        try { var address = new MailAddress(value); return address.Address == value && address.User.Length > 0 && address.Host.Length > 0; }
        catch (FormatException) { return false; }
    }
}
