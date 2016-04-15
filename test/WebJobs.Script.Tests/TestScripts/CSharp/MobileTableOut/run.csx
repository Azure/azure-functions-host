using System;
using Microsoft.Azure.WebJobs.Host;

public static void Run(string input, out Item item, TraceWriter log)
{
    item = new Item
    {
        Id = input,
        Text = "Hello from C#!"
    };

    log.Verbose($"Inserting item {item.Id}");
}

public class Item
{
    public string Id { get; set; }
    public string Text { get; set; }
    public bool IsProcessed { get; set; }
    public DateTimeOffset ProcessedAt { get; set; }

    // Mobile table properties
    public DateTimeOffset CreatedAt { get; set; }
}