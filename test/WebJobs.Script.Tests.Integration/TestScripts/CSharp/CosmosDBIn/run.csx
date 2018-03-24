#r "Newtonsoft.Json"

using System;
using System.Linq;
using Newtonsoft.Json.Linq;

public static void Run(QueueInput input, JObject item, IEnumerable<dynamic> relatedItems)
{
    // throw if there's not 3 items
    int count = relatedItems.Count();
    if (count != 3)
    {
        throw new InvalidOperationException($"Expected 3 documents. Found {count}.");
    }

    item["text"] = "This was updated!";
}

public class QueueInput
{
    public string DocumentId { get; set; }
}