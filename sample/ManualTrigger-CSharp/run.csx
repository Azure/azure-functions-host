public static void Run(Input input, string blobIn, out string blobOut, TraceWriter log)
{
    log.Info($"C# manually triggered function called: InId: {input.InId}, OutId: {input.OutId}");

    blobOut = blobIn; 
}

public class Input
{
    public string InId { get; set; }
    public string OutId { get; set; }
}