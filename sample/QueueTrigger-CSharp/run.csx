#load "..\Shared\Message.csx"

using System;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.Azure.WebJobs.Host;

public static void Run(Message message, out string result, TraceWriter log)
{
    log.Info($"C# Queue trigger function processed message: {message.Id}");

    result = message.Value;
}