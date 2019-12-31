#r "Microsoft.Azure.Storage"

using Microsoft.Azure.Storage.Blob;

public static async Task Run(CloudBlockBlob blob, CloudBlockBlob output, TraceWriter log)
{
    string content = await blob.DownloadTextAsync();
    log.Info($"C# Blob trigger function processed a blob. Blob={content}");

    await output.UploadTextAsync(content);
}