
using System;

public class SampleEntity
{
    public int Id { get; set; }
    public string Text { get; set; }
}

public static void Run(string text, SampleEntity entity, TraceWriter log)
{
    entity.Text = text;
}

