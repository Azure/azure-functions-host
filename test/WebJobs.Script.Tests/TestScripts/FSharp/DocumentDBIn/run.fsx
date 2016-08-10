﻿//----------------------------------------------------------------------------------------
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
type QueueInput =
    { DocumentId : string }

let Run(input: QueueInput, item: JObject) =
    async {
        item.["text"] <- JToken.op_Implicit "This was updated!"
    } |> Async.StartAsTask
