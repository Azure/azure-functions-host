#r "Newtonsoft.Json"

using System;
using System.Net;
using System.Threading.Tasks;
using Newtonsoft.Json;

public static async Task<object> Run(HttpRequestMessage req, TraceWriter log)
{
    string json = await req.Content.ReadAsStringAsync();
    Payload payload = JsonConvert.DeserializeObject<Payload>(json);

    return req.CreateResponse(HttpStatusCode.OK, $"Value: {payload.Value}");
}

public class Payload
{
    public string Id { get; set; }
    public string Value { get; set; }
}
