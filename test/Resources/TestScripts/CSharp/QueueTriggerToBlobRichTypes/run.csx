#r "Azure.Storage.Blobs"
using Azure.Storage.Blobs;

public static async Task Run(WorkItem input, BlobClient output, TraceWriter log)
{
    string json = string.Format("{{ \"id\": \"{0}\" }}", input.Id);

    log.Info($"C# script processed queue message. Item={json}");

    await output.UploadAsync(BinaryData.FromString(json), overwrite: true);
}

public class WorkItem
{
    public string Id { get; set; }
}