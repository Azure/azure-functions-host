using System.Net;
using System.Net.Http.Headers;

public static async Task<HttpResponseMessage> Run(HttpResponseMessage res, TraceWriter log)
{
    HttpResponseMessage response = new HttpResponseMessage();
    response.Headers.Add("ResponseServerDateTime", DateTime.Now.ToString());

    return response;
}
