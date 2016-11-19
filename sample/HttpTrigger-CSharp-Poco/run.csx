#r "System.Runtime.Serialization"

using System.Net;
using System.Runtime.Serialization;

// DataContract attributes exist to demonstrate that
// XML payloads are also supported
[DataContract(Name = "RequestData", Namespace = "http://functions")]
public class RequestData
{
    [DataMember]
    public string Id { get; set; }
    [DataMember]
    public string Value { get; set; }
}

public static HttpResponseMessage Run(RequestData data, HttpRequestMessage req, out string outBlob, ExecutionContext context, TraceWriter log)
{
    log.Info($"C# HTTP trigger function processed a request. {req.RequestUri}");
    log.Info($"InvocationId: {context.InvocationId}");

    outBlob = data.Value;

    return new HttpResponseMessage(HttpStatusCode.OK);
}