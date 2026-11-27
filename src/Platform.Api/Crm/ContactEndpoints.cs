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
        endpoints.MapPost("/api/v1/contacts",CreateAsync).RequireAuthorization().WithMetadata(new RequireAccessToken());
        endpoints.MapGet("/api/v1/contacts/{id}",GetAsync).RequireAuthorization().WithMetadata(new RequireAccessToken());
        return endpoints;
    }

    private static Task Error(HttpContext context,int status,string reason) => OperationalErrors.WriteProblemAsync(context,status,reason,context.RequestAborted);

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
        byte[]? cached = null;
        await TenantEndpointTransaction.RunAsync(context,configuration,true,async (connection,transaction,tenant,commit) =>
        {
            if (!context.Request.HasJsonContentType()) { await Error(context,415,"REQUEST_INVALID"); return; }
            string? name=null,email=null,phone=null;
            var phonePresent=false;
            try
            {
                if (cached is null)
                {
                    const int maxBytes=8192;
                    using var buffer=new MemoryStream(); var chunk=new byte[1024]; int count;
                    while ((count=await context.Request.Body.ReadAsync(chunk,context.RequestAborted)) != 0)
                    {
                        if (buffer.Length+count>maxBytes) { await Error(context,413,"REQUEST_INVALID"); return; }
                        buffer.Write(chunk,0,count);
                    }
                    cached=buffer.ToArray();
                }
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
            ContactRecord contact;
            string action;
            if (email is not null)
            {
                var current=await ContactStore.LockByEmailAsync(connection,transaction,tenant,email,context.RequestAborted);
                if (current is not null)
                {
                    if (precondition == MembershipPreconditionResult.Missing) { await Error(context,428,"PRECONDITION_REQUIRED"); return; }
                    if (version != current.Version) { await Error(context,412,"VERSION_CONFLICT"); return; }
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
                    try { contact=await ContactStore.CreateAsync(connection,transaction,tenant,name,email,phone,context.RequestAborted); }
                    catch (PostgresException ex) when (ex.SqlState == PostgresErrorCodes.UniqueViolation)
                    { await Error(context,409,"CONTACT_EMAIL_CONFLICT"); return; }
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

    private static bool ValidText(string value)
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
