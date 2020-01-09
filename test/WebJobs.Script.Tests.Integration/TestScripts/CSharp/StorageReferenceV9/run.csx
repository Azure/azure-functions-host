// Use storage v9
#r "Microsoft.WindowsAzure.Storage"
using Microsoft.WindowsAzure.Storage.Blob;

public static void Run(string input, TraceWriter log)
{
    // it is enough to just reference the type - as long as compilation is successful, we're good
    CloudBlockBlob blob;
    log.Info(input);
}