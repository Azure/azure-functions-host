//----------------------------------------------------------------------------------------
// This prelude allows scripts to be edited in Visual Studio or another F# editing environment 

#if !COMPILED
#I "../../../../../src/WebJobs.Script.Host/bin/Debug"
#r "Microsoft.Azure.WebJobs.Host.dll"
#r "Microsoft.Azure.WebJobs.Extensions.dll"
#endif

//----------------------------------------------------------------------------------------
// This is the implementation of the function 

#r "Newtonsoft.Json"

open System
open Microsoft.Azure.WebJobs.Host

[<CLIMutable>]
type ScenarioInput =
    { Scenario : string
      Container : string
      Value: string } 

let Run(input: ScenarioInput, blob: byref<string>, log: TraceWriter) =
    if (input.Scenario = "randGuid") then
        blob <- input.Value
    else
        failwith (sprintf "The scenario '%s' did not match any known scenario." input.Scenario)

