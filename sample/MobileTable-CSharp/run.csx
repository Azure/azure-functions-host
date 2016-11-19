public static Item Run(string input)
{
    return new Item
    {
        Text = "Hello from C#! " + input
    };    
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