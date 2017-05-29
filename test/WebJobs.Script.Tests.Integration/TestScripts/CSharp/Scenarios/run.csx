#r "Newtonsoft.Json"

public static void Run(ScenarioInput input, out string blob, TraceWriter trace, ILogger logger)
{
    blob = null;

    if (input.Scenario == "randGuid")
    {
        blob = input.Value;
    }
    else if (input.Scenario == "fileLogging")
    {
        // we'll get two guids as the payload; split them
        var guids = input.Value.Split(';');
        trace.Info($"From TraceWriter: {guids[0]}");
        logger.LogInformation($"From ILogger: {guids[1]}");
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