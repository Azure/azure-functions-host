//----------------------------------------------------------------------------------------
// This prelude allows scripts to be edited in Visual Studio or another F# editing environment 

#if !COMPILED
#I "../../../../../bin/Binaries/WebJobs.Script.Host"
#r "Microsoft.Azure.WebJobs.Host.dll"
#r "Microsoft.Azure.WebJobs.Extensions.dll"
#endif

//----------------------------------------------------------------------------------------
// This is the implementation of the function 

#load "class.fs"

open System.Net
open System.Diagnostics

let Run(req: HttpRequestMessage) : Task<HttpResponseMessage>  = 
    async {
        let response = Test().Response
        req.Properties.["LoadedScriptResponse"] <- response

        return new HttpResponseMessage(HttpStatusCode.OK, Content = new StringContent(response)) 
    } |> Async.StartAsTask
