#r "Newtonsoft.Json"

using System.Collections.Generic;
using Newtonsoft.Json.Linq;
using Newtonsoft.Json;

public static void Run(ScenarioInput input, out string blob, TraceWriter trace, ILogger logger, ExecutionContext context)
{
    blob = null;

    if (input.Scenario == "randGuid")
    {
        blob = input.Value;
    }
    else if (input.Scenario == "logging")
    {
        // we'll get two guids as the payload; split them
        var guids = input.Value.Split(';');
        trace.Info($"From TraceWriter: {guids[0]}");
        logger.LogInformation($"From ILogger: {guids[1]}");
    }
    else if (input.Scenario == "appInsights")
    {
        var logPayload = new
        {
            invocationId = context.InvocationId,
            trace = input.Value
        };

        logger.LogInformation(JObject.FromObject(logPayload).ToString(Formatting.None));
        logger.LogMetric("TestMetric", 1234, new Dictionary<string, object>
        {
            ["Count"] = 50,
            ["Min"] = 10.4,
            ["Max"] = 23,
            ["MyCustomMetricProperty"] = 100
        });
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