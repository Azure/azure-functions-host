#load "..\Shared\Message.csx"

public static string Run(Message message, ILogger log)
{
    log.LogInformation($"C# Queue trigger function processed message: {message.Id}");
    return message.Value;
}