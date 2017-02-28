﻿#r "Microsoft.Azure.ApiHub.Sdk"

using Microsoft.Azure.ApiHub;

public class SampleEntity
{
    public int Id { get; set; }
    public string Text { get; set; }
}

public static async Task Run(string text, ITableClient client, TraceWriter log)
{
    var dataSet = client.GetDataSetReference();
    var table = dataSet.GetTableReference<SampleEntity>("SampleTable");

    await table.UpdateEntityAsync(
        "1",
        new SampleEntity
        {
            Id = 1,
            Text = text
        });
}
