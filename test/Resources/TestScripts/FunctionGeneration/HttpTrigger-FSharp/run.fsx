#r "System.Threading.Tasks"
#r "System.Net.Http"

open System
open System.Linq
open System.Net
open System.Net.Http
open System.Threading.Tasks
open System.Diagnostics
open Microsoft.Azure.WebJobs.Host


let MyAsyncMethod() = async { do! Async.Sleep 500 }

let Run(req: HttpRequestMessage ) =
   async { 
    // DO NOT modify this RunSynchronously as we want the test to run with it in place to ensure it won't cause a deadlock.
    do MyAsyncMethod() |> Async.RunSynchronously

    return new HttpResponseMessage(HttpStatusCode.OK)
   } |> Async.StartAsTask



