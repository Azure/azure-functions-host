#r "Newtonsoft.Json"

using System;
using System.Net;
using System.Threading.Tasks;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

public static async Task<object> Run(HttpRequestMessage req, TraceWriter log)
{
    string json = await req.Content.ReadAsStringAsync();
    Payload payload = JsonConvert.DeserializeObject<Payload>(json);

    JObject body = new JObject()
    {
        { "result", $"Value: {payload.Value}" }
    };
    return req.CreateResponse(HttpStatusCode.OK, body, "application/json");
}

public class Payload
{
    public string Id { get; set; }
    public string Value { get; set; }
}
