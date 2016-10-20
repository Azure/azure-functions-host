using System;
using System.Net;
using System.Net.Http;
using Microsoft.Azure.WebJobs.Host;

public class BotMessage
{
    public string Source { get; set; }
    public string Message { get; set; }
}

public static HttpResponseMessage Run(string input, out BotMessage message, TraceWriter log)
{
    log.Info($"Sending Bot message {input}");

    message = new BotMessage
    {
        Source = "Azure Functions (C#)!",
        Message = input
    };

    return new HttpResponseMessage(HttpStatusCode.OK);
}