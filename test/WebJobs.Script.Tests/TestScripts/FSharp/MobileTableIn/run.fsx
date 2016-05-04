#r "Newtonsoft.Json"

open System
open Microsoft.Azure.WebJobs.Host
open Newtonsoft.Json.Linq

let Run(input: string, item: JObject, log: TraceWriter) =

    item.["Text"] <- "This was updated!"

    log.Info(sprintf "Updating item %s" item.["Id"])
