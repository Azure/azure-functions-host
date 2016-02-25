#r "System.Web.Http"

using System;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Threading.Tasks;
using System.Diagnostics;
using Microsoft.Azure.WebJobs.Host;

public static Task<HttpResponseMessage> Run(HttpRequestMessage req, TraceWriter log)
{
    var queryParamms = req.GetQueryNameValuePairs()
        .ToDictionary(p => p.Key, p => p.Value, StringComparer.OrdinalIgnoreCase);

    log.Verbose(string.Format("CSharp HTTP trigger function processed a request. Name={0}", req.RequestUri));

    HttpResponseMessage res = null;
    string name;
    if (queryParamms.TryGetValue("name", out name))
    {
        res = new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent("Hello " + name)
        };
    }
    else
    {
        res = new HttpResponseMessage(HttpStatusCode.BadRequest)
        {
            Content = new StringContent("Please pass a name on the query string")
        };
    }

    return Task.FromResult(res);
}