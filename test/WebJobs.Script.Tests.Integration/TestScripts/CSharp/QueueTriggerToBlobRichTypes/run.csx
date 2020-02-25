#r "Microsoft.Azure.Storage.Blob"
using Microsoft.Azure.Storage.Blob;

public static async Task Run(WorkItem input, CloudBlockBlob output, TraceWriter log)
{
    string json = string.Format("{{ \"id\": \"{0}\" }}", input.Id);

    log.Info($"C# script processed queue message. Item={json}");
    
    await output.UploadTextAsync(json);
}

public class WorkItem
{
    public string Id { get; set; }
}