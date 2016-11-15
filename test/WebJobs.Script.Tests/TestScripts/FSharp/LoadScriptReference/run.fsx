#r "System.Threading.Tasks"
#r "System.Net.Http"

#load "class.fs"

open System
open System.Net
open System.Net.Http
open System.Threading.Tasks
open System.Diagnostics
open Microsoft.Azure.WebJobs.Host

let Run(req: HttpRequestMessage) : Task<HttpResponseMessage>  = 
    async {
        let response = Class.Test().Response
        req.Properties.["LoadedScriptResponse"] <- response

        return new HttpResponseMessage(HttpStatusCode.OK, Content = new StringContent(response)) 
    } |> Async.StartAsTask
