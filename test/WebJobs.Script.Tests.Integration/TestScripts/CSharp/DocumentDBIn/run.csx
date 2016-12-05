#r "Newtonsoft.Json"

using Newtonsoft.Json.Linq;

public static void Run(QueueInput input, JObject item)
{
    item["text"] = "This was updated!";
}

public class QueueInput
{
    public string DocumentId { get; set; }
}