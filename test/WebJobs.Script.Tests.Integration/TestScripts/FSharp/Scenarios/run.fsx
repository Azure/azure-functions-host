#r "Newtonsoft.Json"

open System
open Microsoft.Azure.WebJobs.Host

[<CLIMutable>]
type ScenarioInput =
    { Scenario : string
      Container : string
      Value: string } 

let Run(input: ScenarioInput, blob: byref<string>, trace: TraceWriter, logger: ILogger) =
    if (input.Scenario = "randGuid") then
        blob <- input.Value
    else if (input.Scenario = "fileLogging") then
        let guids = input.Value.Split(';')
        trace.Info(sprintf "From TraceWriter: %s" guids.[0])
        logger.LogInformation(sprintf "From ILogger: %s" guids.[1])
    else
        failwith (sprintf "The scenario '%s' did not match any known scenario." input.Scenario)

