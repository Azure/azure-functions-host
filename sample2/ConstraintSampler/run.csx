#r "Microsoft.WindowsAzure.Storage"
using System.Net;
using Microsoft.WindowsAzure.Storage.Table;
using System.Net.Http;
using System.Text;
using System;

public static async Task<HttpResponseMessage> Run(HttpRequestMessage req, TraceWriter log, string lenvalue, string alphavalue, long fixedint)
{
    HttpResponseMessage res = null;
    res = new HttpResponseMessage(HttpStatusCode.OK)
    {
        Content = new StringContent("passed all constraints")
    };
    return res;
}
