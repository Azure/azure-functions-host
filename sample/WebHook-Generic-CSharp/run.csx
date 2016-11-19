#r "Newtonsoft.Json"

using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

public static JObject Run(Payload payload, string action)
{
    JObject body = new JObject()
    {
        { "result", $"Value: {payload.Value} Action: {action}" }
    };
    return body;
}

public class Payload
{
    public string Id { get; set; }
    public string Value { get; set; }
}
