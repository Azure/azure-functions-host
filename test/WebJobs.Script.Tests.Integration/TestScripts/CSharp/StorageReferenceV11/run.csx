// Use storage v11
#r "Microsoft.Azure.Storage.Blob"

using Microsoft.Azure.Storage.Blob;

public static void Run(string input, TraceWriter log)
{
    // it is enough to just reference the type - as long as compilation is successful, we're good
    CloudBlockBlob blob;
    log.Info(input);
}