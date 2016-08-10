//----------------------------------------------------------------------------------------
// This prelude allows scripts to be edited in Visual Studio or another F# editing environment 

#if !COMPILED
#I "../../../../../bin/Binaries/WebJobs.Script.Host"
#r "Microsoft.Azure.WebJobs.Host.dll"
#r "Microsoft.Azure.WebJobs.Extensions.dll"
#endif

//----------------------------------------------------------------------------------------
// This is the implementation of the function 

#r "Newtonsoft.Json"

open System
open Microsoft.Azure.WebJobs.Host
open Newtonsoft.Json.Linq

[<CLIMutable>]
type TestInput = { Id : int; Value: string }

[<CLIMutable>]
type SampleEntity = { Id: int; Text: string }

let Run(input: TestInput, entity: byref<SampleEntity>, log: TraceWriter ) = 
    if (entity.Id <> input.Id) then
        invalidOp ("Expected Id to be bound.")

    entity <- { entity with Text = input.Value }

