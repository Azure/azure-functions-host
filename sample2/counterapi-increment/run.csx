#r "Microsoft.WindowsAzure.Storage"
using System.Net;
using Microsoft.WindowsAzure.Storage;
using Microsoft.WindowsAzure.Storage.Table;

public static async Task<HttpResponseMessage> Run(HttpRequestMessage req, TraceWriter log, CloudTable tableBinding, string countername, int add)
{
    HttpResponseMessage res = null;
    try
    {
        var getCurrentValue = new TableQuery<Counter>().Where(TableQuery.GenerateFilterCondition("RowKey", QueryComparisons.Equal, countername));

        Counter counter = tableBinding.ExecuteQuery(getCurrentValue).First();
        counter.Value += add;

        TableOperation updateOperation = TableOperation.Replace(counter);
        tableBinding.Execute(updateOperation);
        res = new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent("Updated the value of the counter by " + add + ".")
        };
    }
    catch (Exception e)
    {
        res = new HttpResponseMessage(HttpStatusCode.BadRequest)
        {
            Content = new StringContent(countername + " " + add)
        };
    }

    return res;
}


public class Counter : TableEntity
{
    public int Value { get; set; }
}