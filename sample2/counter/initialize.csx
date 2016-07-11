#r "Microsoft.WindowsAzure.Storage"
using System.Net;
using Microsoft.WindowsAzure.Storage.Table;
using System.Net.Http;

public class Counter : TableEntity
{
	public int Value { get; set; }
}

public static async Task<HttpResponseMessage> Run(HttpRequestMessage req, TraceWriter log, ICollector<Counter> counter, String countername)
{
	HttpResponseMessage res = null;
	try
	{
		counter.Add(
			new Counter()
			{
				RowKey = countername,
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
		res = new HttpResponseMessage(HttpStatusCode.BadRequest)
		{
			Content = new StringContent("The counter already exists")
		};
	}
	return res;
}