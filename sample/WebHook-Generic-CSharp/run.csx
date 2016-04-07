#r "Newtonsoft.Json"

using System;
using System.Net;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

public static JObject Run(Payload payload)
{
    JObject body = new JObject()
    {
        { "result", $"Value: {payload.Value}" }
    };
    return body;
}

public class Payload
{
    public string Id { get; set; }
    public string Value { get; set; }
}
