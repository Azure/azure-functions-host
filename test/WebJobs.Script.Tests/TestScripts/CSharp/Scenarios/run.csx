#r "Newtonsoft.Json"

using System;
using System.IO;
using Newtonsoft.Json;

public static void Run(ScenarioInput input, out string blob, TraceWriter log)
{
    blob = null;

    if (input.Scenario == "randGuid")
    {
        blob = input.Value;
    }
    else
    {
        throw new NotSupportedException($"The scenario '{input.Scenario}' did not match any known scenario.");
    }
}

public class ScenarioInput
{
    public string Scenario { get; set; }
    public string Container { get; set; }
    public string Value { get; set; }
}