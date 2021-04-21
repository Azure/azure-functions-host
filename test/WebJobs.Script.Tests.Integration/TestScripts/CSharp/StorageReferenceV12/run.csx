// Use storage v12
#r "Azure.Storage.Blobs"

using Azure.Storage.Blobs;

public static void Run(string input, TraceWriter log)
{
    // it is enough to just reference the type - as long as compilation is successful, we're good
    BlobClient blobClient;
    log.Info(input);
}