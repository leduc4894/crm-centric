using System.Net;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using Corvus.Text.Json;
using Corvus.Text.Json.OpenApi.HttpTransport;

const string id = "00000000-0000-4000-8000-000000000001";
const string correlation = "sample-correlation-01";
static void Check(bool condition) { if (!condition) throw new InvalidOperationException("SDK_CONSUMER_ASSERTION_FAILED"); }
static string Problem(int status) => $"{{\"type\":\"urn:sample\",\"title\":\"sample\",\"status\":{status},\"reasonToken\":\"TEST_{status}\",\"correlationId\":\"sample-correlation-01\"}}";
static HttpResponseMessage Reply(int status, string body, bool etag = false) {
    var result = new HttpResponseMessage((HttpStatusCode)status) { Content = new StringContent(body, Encoding.UTF8, status >= 400 ? "application/problem+json" : "application/json") };
    result.Headers.Add("X-Correlation-Id", correlation);
    if (etag) result.Headers.ETag = new EntityTagHeaderValue("\"v1\"");
    return result;
}
var handler = new CaptureHandler();
using var http = new HttpClient(handler) { BaseAddress = new Uri("https://example.invalid") };
await using var transport = new HttpClientTransport(http);
await using var op = new Platform.Operations.Client.ApiDefaultClient(transport);
foreach (int status in new[] {200, 500, 503}) {
    handler.Respond = (request, _) => {
        Check(request.RequestUri!.AbsolutePath == "/health/ready");
        Check(!request.Headers.Contains("Authorization") && !request.Headers.Contains("X-Tenant-Id") && !request.Headers.Contains("Cookie"));
        return Task.FromResult(Reply(status, status == 200 ? "{\"status\":\"ready\"}" : Problem(status)));
    };
    await using var response = await op.GetHealthReadyAsync();
    Check(response.StatusCode == status && response.XCorrelationIdHeader.ToString().Contains(correlation));
    if (status == 200) Check(response.OkBody.ToString().Contains("ready"));
    else {
        string body = status == 500 ? response.InternalServerErrorBody.ToString() : response.ServiceUnavailableBody.ToString();
        Check(JsonNode.Parse(body)!["reasonToken"]!.GetValue<string>() == $"TEST_{status}");
    }
}
using var businessHttp = new HttpClient(handler, disposeHandler: false) { BaseAddress = new Uri("https://example.invalid") };
businessHttp.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", "synthetic-secret-sentinel-not-a-credential");
await using var businessTransport = new HttpClientTransport(businessHttp);
await using var business = new Crm.Client.ApiDefaultClient(businessTransport);
handler.Respond = (request, _) => {
    Check(request.Headers.GetValues("X-Tenant-Id").Single() == id);
    Check(request.Headers.Authorization?.Parameter == "synthetic-secret-sentinel-not-a-credential");
    Check(request.RequestUri!.AbsolutePath.EndsWith(id));
    return Task.FromResult(Reply(200, "{\"id\":\"" + id + "\",\"displayName\":\"sample\",\"email\":null}", true));
};
await using (var get = await business.GetContactAsync(id, id)) {
    Check(get.StatusCode == 200);
    Check(get.ETagHeader.ToString().Contains("v1"));
}
foreach (int status in new[] {412, 428}) {
    int before = handler.Calls;
    handler.Respond = async (request, _) => {
        Check(request.Headers.GetValues("If-Match").Single() == "\"v1\"");
        var json = JsonNode.Parse(await request.Content!.ReadAsStringAsync())!;
        Check(json["email"] is null && !json.AsObject().ContainsKey("tenant_id"));
        return Reply(status, Problem(status));
    };
    using var body = ParsedJsonDocument<Crm.Client.Models.UpdateContact>.Parse("{\"email\":null}");
    await using var result = await business.UpdateContactAsync(id, id, "\"v1\"", body.RootElement);
    Check(result.StatusCode == status && handler.Calls == before + 1);
    var detail = JsonNode.Parse(status == 412 ? result.Status412Body.ToString() : result.Status428Body.ToString())!;
    Check(detail["reasonToken"]!.GetValue<string>() == $"TEST_{status}" && detail["correlationId"]!.GetValue<string>() == correlation);
}
handler.Respond = (request, _) => {
    var query = request.RequestUri!.Query;
    Check(query.Contains("cursor=a%2Bb%2F%3D%3F", StringComparison.OrdinalIgnoreCase));
    Check(query.Contains("tagId=" + id) && query.Contains("limit=42"));
    Check(query.Contains("updatedSince=2026-01-01T00%3A00%3A00Z", StringComparison.OrdinalIgnoreCase));
    return Task.FromResult(Reply(200, "{\"items\":[],\"nextCursor\":null}"));
};
await using (var page = await business.ListContactsAsync(id, limit: 42, cursor: "a+b/=?", tagId: id, updatedSince: "2026-01-01T00:00:00Z")) { Check(page.StatusCode == 200); }
handler.Respond = (request, _) => {
    Check(request.Headers.Authorization is null && !request.Headers.Contains("X-Tenant-Id"));
    return Task.FromResult(Reply(200, "{\"status\":\"ready\"}"));
};
await using (var anon = await op.GetHealthReadyAsync()) { Check(anon.StatusCode == 200); }
var cancel = new CancellationTokenSource();
cancel.Cancel();
handler.Respond = (_, token) => { token.ThrowIfCancellationRequested(); throw new InvalidOperationException("SDK_CANCEL_FAILED"); };
try { await op.GetHealthReadyAsync(cancel.Token); throw new InvalidOperationException("SDK_CANCEL_FAILED"); }
catch (OperationCanceledException) { }
using (var valid = ParsedJsonDocument<Crm.Client.Models.CreateContact>.Parse("{\"displayName\":\"sample\",\"email\":null}")) { Check(valid.RootElement.EvaluateSchema()); }
using (var missing = ParsedJsonDocument<Crm.Client.Models.CreateContact>.Parse("{\"email\":null}")) { Check(!missing.RootElement.EvaluateSchema()); }
using (var extra = ParsedJsonDocument<Crm.Client.Models.CreateContact>.Parse("{\"displayName\":\"sample\",\"tenant_id\":\"sample\"}")) { Check(!extra.RootElement.EvaluateSchema()); }
foreach (var sample in new [] {"{\"ruleId\":\"sample\",\"version\":1,\"triggerEventType\":\"crm.contact.created.v1\",\"condition\":{\"field\":\"/data/score\",\"op\":\"gt\",\"value\":100},\"actions\":[{\"type\":\"crm.createNote.v1\",\"input\":{\"text\":\"sample\"}},{\"type\":\"module.send.v1\",\"installationId\":\"00000000-0000-4000-8000-000000000001\",\"input\":{\"message\":\"sample\"}}]}", "{\"ruleId\":\"sample\",\"version\":1,\"triggerEventType\":\"crm.contact.created.v1\",\"condition\":{\"all\":[{\"field\":\"/data/score\",\"op\":\"eq\",\"value\":null}]},\"actions\":[{\"type\":\"crm.addTag.v1\",\"input\":{\"tag\":\"sample\"}}]}"}) {
    using var parsed = ParsedJsonDocument<Crm.Client.Models.AutomationRule>.Parse(sample);
    Check(JsonNode.DeepEquals(JsonNode.Parse(sample), JsonNode.Parse(parsed.RootElement.ToString())));
    handler.Respond = async (request, _) => {
        Check(JsonNode.DeepEquals(JsonNode.Parse(sample), JsonNode.Parse(await request.Content!.ReadAsStringAsync())));
        return Reply(201, sample);
    };
    await using var created = await business.CreateRuleAsync(id, parsed.RootElement);
    Check(created.StatusCode == 201 && JsonNode.DeepEquals(JsonNode.Parse(sample), JsonNode.Parse(created.CreatedBody.ToString())));
}
Console.WriteLine("DOTNET_WIRE_OK");

sealed class CaptureHandler : HttpMessageHandler {
    public int Calls { get; private set; }
    public Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> Respond { get; set; } = (_, _) => throw new InvalidOperationException("SDK_HANDLER_MISSING");
    protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) { Calls++; return Respond(request, cancellationToken); }
}
