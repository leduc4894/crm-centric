using System.Net;
using System.Net.Http.Headers;
using System.Text;
using Corvus.Text.Json;
using Corvus.Text.Json.OpenApi.HttpTransport;

const string id = "00000000-0000-4000-8000-000000000001";
var handler = new FixtureHandler();
using var http = new HttpClient(handler) { BaseAddress = new Uri("https://example.invalid") };
await using var transport = new HttpClientTransport(http);
await using var api = new Crm.Client.ApiDefaultClient(transport);
using var input = ParsedJsonDocument<Crm.Client.Models.CreateContact>.Parse("{\"displayName\":\"Example\",\"email\":\"example@example.test\"}");
foreach (int status in new[] { 201, 200, 400, 412, 428 })
{
    handler.Status = status;
    await using var response = status == 201 ? await api.CreateContactAsync(id, input.RootElement) : await api.CreateContactAsync(id, input.RootElement, "\"v1\"", id);
    if (response.StatusCode != status) throw new Exception("status mismatch");
    var classification = response.MatchResult<string>(
        _ => "updated", _ => "created", _ => "error", _ => "error", _ => "error", _ => "error", _ => "error",
        _ => "error", _ => "error", _ => "error", _ => "error", _ => "error", _ => "error", _ => "error");
    if (classification != (status == 201 ? "created" : status == 200 ? "updated" : "error")) throw new Exception("classification mismatch");
    if (status == 412 && !response.Status412Body.ToString().Contains("TEST")) throw new Exception("missing conflict problem");
    if (status == 428 && !response.Status428Body.ToString().Contains("TEST")) throw new Exception("missing precondition problem");
    if (status == 400 && !response.BadRequestBody.ToString().Contains("TEST")) throw new Exception("missing validation problem");
    if (status < 300)
    {
        Crm.Client.Models.PostApiV1ContactsOkETag etag = response.ETagHeader;
        if (!etag.ToString().Contains("v1")) throw new Exception("missing ETag");
        var contact = status == 200 ? response.OkBody : response.CreatedBody;
        if (!contact.ToString().Contains("Example")) throw new Exception("invalid contact");
    }
}
Console.WriteLine("NEW_UPSERT_CONSUMER_OK");
sealed class FixtureHandler : HttpMessageHandler
{
    public int Status { get; set; }
    protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        if (request.Headers.GetValues("X-Tenant-Id").Single() != "00000000-0000-4000-8000-000000000001") throw new Exception("tenant absent");
        if (Status == 201 ? request.Headers.Contains("If-Match") : request.Headers.GetValues("If-Match").Single() != "\"v1\"") throw new Exception("invalid precondition");
        if (Status == 201 ? request.Headers.Contains("X-Contact-Id") : request.Headers.GetValues("X-Contact-Id").Single() != "00000000-0000-4000-8000-000000000001") throw new Exception("invalid expected contact ID");
        var body = Status < 300 ? "{\"id\":\"00000000-0000-4000-8000-000000000001\",\"displayName\":\"Example\",\"version\":1,\"createdAt\":\"2026-01-01T00:00:00Z\",\"updatedAt\":\"2026-01-01T00:00:00Z\"}" : "{\"type\":\"urn:example\",\"title\":\"Error\",\"status\":" + Status + ",\"reasonToken\":\"TEST\",\"correlationId\":\"test\"}";
        var response = new HttpResponseMessage((HttpStatusCode)Status) { Content = new StringContent(body, Encoding.UTF8, Status < 300 ? "application/json" : "application/problem+json") };
        if (Status < 300) response.Headers.ETag = new EntityTagHeaderValue("\"v1\"");
        return Task.FromResult(response);
    }
}
