#r "Newtonsoft.Json"

open System
open Microsoft.Azure.WebJobs.Host
open Newtonsoft.Json.Linq

[<CLIMutable>]
type QueueInput = { Id : int; Value: string }

[<CLIMutable>]
type SampleEntity = { Id: int; mutable Text: string }

let Run(input: QueueInput, entity: SampleEntity, log: TraceWriter ) = 
    if (entity.Id <> input.Id) then
        invalidOp "Expected Id to be bound."

    entity.Text <- input.Value 

