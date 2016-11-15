#r "Newtonsoft.Json"

open System
open Microsoft.Azure.WebJobs.Host
open Newtonsoft.Json.Linq

[<CLIMutable>]
type QueueInput = 
   { RecordId : string }

let Run(input: QueueInput, item: JObject, log: TraceWriter) =
    item.["Text"] <- JToken.op_Implicit "This was updated!"
    log.Info(sprintf "Updating item %s" (item.["Id"].ToString()))
