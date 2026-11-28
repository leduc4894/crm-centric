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
foreach (int status in new[] { 201, 200 })
{
    handler.Status = status;
    await using var response = await api.CreateContactAsync(id, input.RootElement);
    Crm.Client.Models.PostApiV1ContactsCreatedETag etag = response.ETagHeader;
    if (!etag.ToString().Contains("v1")) throw new Exception("missing ETag");
    var classification = response.MatchResult<string>(
        _ => "created", _ => "error", _ => "error", _ => "error", _ => "error", _ => "error",
        _ => "error", _ => "error", _ => "error", _ => "error", _ => "error");
    if (classification != (status == 201 ? "created" : "error")) throw new Exception("old consumer changed behavior");
}
Console.WriteLine("OLD_201_ONLY_CONSUMER_OK");
sealed class FixtureHandler : HttpMessageHandler
{
    public int Status { get; set; }
    protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        var response = new HttpResponseMessage((HttpStatusCode)Status) { Content = new StringContent("{\"id\":\"00000000-0000-4000-8000-000000000001\",\"displayName\":\"Example\",\"version\":1,\"createdAt\":\"2026-01-01T00:00:00Z\",\"updatedAt\":\"2026-01-01T00:00:00Z\"}", Encoding.UTF8, "application/json") };
        response.Headers.ETag = new EntityTagHeaderValue("\"v1\"");
        return Task.FromResult(response);
    }
}
