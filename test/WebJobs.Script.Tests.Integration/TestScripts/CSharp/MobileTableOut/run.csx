public static async Task<Item> Run(string input, TraceWriter log)
{
    var item = new Item
    {
        Id = input,
        Text = "Hello from C#!"
    };

    log.Info($"Inserting item {item.Id}");

    // artificially making this function async to test
    // async return values
    await Task.Delay(500);

    return item;
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