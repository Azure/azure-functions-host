#r "Newtonsoft.Json"

open System
open Microsoft.Azure.WebJobs.Host

[<CLIMutable>]
type ScenarioInput =
    { Scenario : string
      Container : string
      Value: string } 

let Run(input: ScenarioInput, blob: byref<string>, log: TraceWriter) =
    blob <- null

    if (input.Scenario = "randGuid") then
        blob <- input.Value;
    else
        failwith (sprintf "The scenario '%s' did not match any known scenario." input.Scenario)

