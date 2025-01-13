#r "Newtonsoft.Json"

using Newtonsoft.Json.Linq;

public static void Run(QueueInput input, JObject item, TraceWriter log)
{
    item["Text"] = "This was updated!";

    log.Info($"Updating item {item["Id"]}");
}

public class QueueInput
{
    public string RecordId { get; set; }
}