#r "Microsoft.Azure.ApiHub.Sdk"

using System;
using Microsoft.Azure.ApiHub.Sdk.Table;

public class TestInput
{
    public int Id { get; set; }
    public string Value { get; set; }
}

public class SampleEntity
{
    public int Id { get; set; }
    public string Text { get; set; }
}

public static async Task Run(TestInput input, ITable<SampleEntity> table, TraceWriter log)
{
    await table.UpdateEntityAsync(
        input.Id.ToString(),
        new SampleEntity
        {
            Id = input.Id,
            Text = input.Value
        });
}

