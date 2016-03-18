using System;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.Azure.WebJobs.Host;

public static void Run(Message message, out string result, TraceWriter log)
{
    log.Verbose($"C# Queue trigger function processed message: {message.Id}");

    result = message.Value;
}

public class Message
{
    public string Id { get; set; }
    public string Value { get; set; }
}