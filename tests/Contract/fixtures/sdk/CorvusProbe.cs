using Corvus.Text.Json;
using Crm.Client.Models;
using System.Text.Json.Nodes;
var samples = new [] {
 "{\"ruleId\":\"sample\",\"version\":1,\"triggerEventType\":\"crm.contact.created.v1\",\"condition\":{\"field\":\"/data/score\",\"op\":\"gt\",\"value\":100},\"actions\":[{\"type\":\"crm.createNote.v1\",\"input\":{\"text\":\"sample\"}},{\"type\":\"module.send.v1\",\"installationId\":\"00000000-0000-4000-8000-000000000001\",\"input\":{\"message\":\"sample\"}}]}",
 "{\"ruleId\":\"sample\",\"version\":1,\"triggerEventType\":\"crm.contact.created.v1\",\"condition\":{\"all\":[{\"field\":\"/data/score\",\"op\":\"eq\",\"value\":null}]},\"actions\":[{\"type\":\"crm.addTag.v1\",\"input\":{\"tag\":\"sample\"}}]}"};
foreach(var s in samples) { using var doc=ParsedJsonDocument<AutomationRule>.Parse(s); var item=doc.RootElement; if (!item.EvaluateSchema() || !JsonNode.DeepEquals(JsonNode.Parse(s),JsonNode.Parse(item.ToString()))) throw new InvalidOperationException("SDK_RULE_ROUNDTRIP_FAILED"); Console.WriteLine("SDK_RULE_ROUNDTRIP_OK"); }
