#r "Microsoft.WindowsAzure.Storage"
using System.Net;
using Microsoft.WindowsAzure.Storage.Table;
using System.Net.Http;
using System.Text;
using System;

public static async Task<HttpResponseMessage> Run(HttpRequestMessage req, TraceWriter log, string strvalue, bool boolvalue, long longvalue, double doubvalue, DateTime date)
{
    var responseText = new StringBuilder();
    responseText.Append("String value: " + strvalue + "\n");
    responseText.Append("Bool value: " + boolvalue + "\n");
    responseText.Append("Long value: " + longvalue + "\n");
    responseText.Append("Double value: " + doubvalue + "\n");
    responseText.Append("Date value: " + date.ToString() + "\n");
    HttpResponseMessage res = null;
    try
    {
        res = new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent(responseText.ToString())
        };
    }
    catch
    {
        res = new HttpResponseMessage(HttpStatusCode.BadRequest)
        {
            Content = new StringContent("The counter has not properly initialized.")
        };
    }
    return res;
}
