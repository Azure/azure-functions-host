#r "Microsoft.WindowsAzure.Storage"
using System.Net;
using Microsoft.WindowsAzure.Storage.Table;
using System.Net.Http;

public static async Task<HttpResponseMessage> Run(HttpRequestMessage req, TraceWriter log, IQueryable<Counter> counters, string countername)
{
    HttpResponseMessage res = null;
    try
    {
        Counter counter = counters.Where(c => c.RowKey.Equals(countername)).First();
        res = new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent("The counter's value is " + counter.Value)
        };
    }
    catch
    {
        res = new HttpResponseMessage(HttpStatusCode.BadRequest)
        {
            Content = new StringContent("The counter has not properly initialized.")
        };
    }
    return res;
}

public class Counter : TableEntity
{
    public int Value { get; set; }
}