#r "Newtonsoft.Json"

using Newtonsoft.Json.Linq;

public static void Run(QueueInput input, JObject item, ILogger log)
{
    item["Text"] = "This was updated!";

    log.LogInformation($"Updating item {item["Id"]}");
}

public class QueueInput
{
    public string RecordId { get; set; }
}