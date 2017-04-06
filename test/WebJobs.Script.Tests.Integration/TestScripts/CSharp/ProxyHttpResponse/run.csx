using System.Net;
using System.Net.Http.Headers;

public static async Task<HttpResponseMessage> Run(HttpResponseMessage res, TraceWriter log)
{
    res.Headers.Add("ResponseServerDateTime", DateTime.Now.ToString());

    return res;
}
