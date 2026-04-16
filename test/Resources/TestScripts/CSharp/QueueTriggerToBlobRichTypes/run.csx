#r "Azure.Storage.Blobs"
using Azure.Storage.Blobs;
using System.IO;

public static async Task Run(WorkItem input, Stream output, TraceWriter log)
{
    string json = string.Format("{{ \"id\": \"{0}\" }}", input.Id);
    log.Info($"C# script processed queue message. Item={json}");

    using (var writer = new StreamWriter(output))
    {
        await writer.WriteAsync(json);
    }
}

public class WorkItem
{
    public string Id { get; set; }
    public string Name { get; set; }
}