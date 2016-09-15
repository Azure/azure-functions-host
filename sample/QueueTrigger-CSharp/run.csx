#load "..\Shared\Message.csx"

using System;
using Microsoft.Azure.WebJobs.Host;

public static string Run(Message message, TraceWriter log)
{
    log.Info($"C# Queue trigger function processed message: {message.Id}");
    return message.Value;
}