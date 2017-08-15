using System.Net;

public static async Task<HttpResponseMessage> Run(HttpRequestMessage req)
{
    return req.CreateResponse(HttpStatusCode.OK, "Time: " + DateTime.Now.ToString());
}