#r "..\..\..\Microsoft.Azure.ApiHub.Sdk.dll"

using System;
using Microsoft.Azure.ApiHub.Sdk.Table;

public class SampleEntity
{
    public int Id { get; set; }
    public string Text { get; set; }
}

public static async Task Run(string text, ITable<SampleEntity> table, TraceWriter log)
{
    await table.UpdateEntityAsync(
        "2",
        new SampleEntity
        {
            Id = 2,
            Text = text
        });
}

