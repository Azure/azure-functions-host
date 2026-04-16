#r "Azure.Storage.Blobs"
#r "Azure.Core"
using Azure.Storage.Blobs;
using System;

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