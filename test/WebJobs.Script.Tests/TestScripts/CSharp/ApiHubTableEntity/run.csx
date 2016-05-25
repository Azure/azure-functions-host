
using System;

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

public static void Run(TestInput input, SampleEntity entity, TraceWriter log)
{
    if (entity.Id != input.Id)
    {
        throw new InvalidOperationException("Expected Id to be bound.");
    }

    entity.Text = input.Value;
}

