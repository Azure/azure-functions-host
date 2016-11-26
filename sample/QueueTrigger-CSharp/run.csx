#load "..\Shared\Message.csx"

public static string Run(Message message, TraceWriter log)
{
    log.Info($"C# Queue trigger function processed message: {message.Id}");
    return message.Value;
}