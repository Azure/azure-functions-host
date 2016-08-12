#r "Microsoft.WindowsAzure.Storage"
using System.Net;
using Microsoft.WindowsAzure.Storage.Table;
using System.Net.Http;
using System.Text;
using System;

public static async Task<HttpResponseMessage> Run(HttpRequestMessage req, TraceWriter log, string strvalue, bool boolvalue, long longvalue, double doubvalue, DateTime date)
{
    //build response text line by line, showcasing each of the parameter values extracted from the route
    var responseText = new StringBuilder();
    responseText.AppendLine("String value: " + strvalue);
    responseText.AppendLine("Bool value: " + boolvalue);
    responseText.AppendLine("Long value: " + longvalue);
    responseText.AppendLine("Double value: " + doubvalue);
    responseText.AppendLine("Date value: " + date.ToString());

    return new HttpResponseMessage(HttpStatusCode.OK)
    {
        Content = new StringContent(responseText.ToString())
    };
}
