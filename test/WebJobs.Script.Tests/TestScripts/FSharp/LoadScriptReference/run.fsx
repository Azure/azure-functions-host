//----------------------------------------------------------------------------------------
// This prelude allows scripts to be edited in Visual Studio or another F# editing environment 

#if !COMPILED
open System.Runtime.InteropServices
#I "../../../../../src/WebJobs.Script.Host/bin/Debug"
#r "Microsoft.Azure.WebJobs.Host.dll"
#r "Microsoft.Azure.WebJobs.Extensions.dll"
#endif

//----------------------------------------------------------------------------------------
// This is the implementation of the function 

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
