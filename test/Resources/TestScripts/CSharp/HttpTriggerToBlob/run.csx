using System;
using System.Collections.Generic;

public class Metadata
{
    public string M1 { get; set; }
    public string M2 { get; set; }
}

public class Input
{
    public string Id { get; set; }
    public string Value { get; set; }
    public Metadata Metadata { get; set; }
}

public static string Run(Input input, IDictionary<string, string> headers, out string outBlob)
{
    var result = input.Value + input.Id + headers["Value"];
    outBlob = result;
    return result;
}