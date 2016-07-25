#r "Microsoft.WindowsAzure.Storage"
using System.Net;
using Microsoft.WindowsAzure.Storage.Table;

public static async Task<HttpResponseMessage> Run(HttpRequestMessage req, TraceWriter log, ICollector<Counter> counter, String counterName)
{
    HttpResponseMessage res = null;
    try
    {
        counter.Add(
            new Counter()
            {
                RowKey = counterName,
                PartitionKey = "items",
                Value = 0
            });
        res = new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent("The counter has been initialized")
        };
    }
    catch
    {
        res = new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent("The counter already exists")
        };
    }
    return res;
}
public class Counter : TableEntity
{
    public int Value { get; set; }
}