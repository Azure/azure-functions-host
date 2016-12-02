#r "System.Threading.Tasks"
#r "..\..\..\Microsoft.WindowsAzure.Mobile.dll"

using Microsoft.WindowsAzure.MobileServices;

public static async Task Run(string input, IMobileServiceTable<Item> table)
{    
    await table.InsertAsync(new Item { Id = input });
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