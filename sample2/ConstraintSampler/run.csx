#r "Microsoft.WindowsAzure.Storage"
using System.Net;
using Microsoft.WindowsAzure.Storage.Table;
using System.Net.Http;
using System.Text;
using System;

public static async Task<HttpResponseMessage> Run(HttpRequestMessage req, TraceWriter log, string lenValue, string alphaValue, long longRange)
{
    //if the function is run, then the parameters passed all of the constraints.
    return new HttpResponseMessage(HttpStatusCode.OK)
    {
        Content = new StringContent("passed all constraints")
    };
}
