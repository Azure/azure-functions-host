public static string Run(Input input, string blobIn, TraceWriter log)
{
    log.Info($"C# manually triggered function called: InId: {input.InId}, OutId: {input.OutId}");
    return blobIn; 
}

public class Input
{
    public string InId { get; set; }
    public string OutId { get; set; }
}