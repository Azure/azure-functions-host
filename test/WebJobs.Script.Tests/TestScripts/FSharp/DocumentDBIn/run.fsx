#r "Newtonsoft.Json"

open System
open Microsoft.Azure.WebJobs.Host
open Newtonsoft.Json.Linq

let Run(input: string, item: JObject) =
    item.["text"] <- "This was updated!"
