open System
open Microsoft.Azure.WebJobs
open Microsoft.Azure.WebJobs.Host

#r "Newtonsoft.Json"
#r "Microsoft.AspNet.WebHooks.Receivers.Azure"

open System
open System.Net
open Newtonsoft.Json
open Newtonsoft.Json.Linq
open Microsoft.AspNet.WebHooks

let Run(payload: AzureAlertNotification ) : JObject = 
    payload.Status <- "Unresolved"
    JObject.FromObject(payload)

