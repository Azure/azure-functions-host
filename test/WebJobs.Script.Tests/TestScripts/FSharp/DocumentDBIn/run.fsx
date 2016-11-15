#r "Newtonsoft.Json"

open System
open Microsoft.Azure.WebJobs.Host
open Newtonsoft.Json.Linq

[<CLIMutable>]
type QueueInput =
    { DocumentId : string }

let Run(input: QueueInput, item: JObject) =
    async {
        item.["text"] <- JToken.op_Implicit "This was updated!"
    } |> Async.StartAsTask
