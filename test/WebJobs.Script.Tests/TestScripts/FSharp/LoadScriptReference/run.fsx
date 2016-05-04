#load "class.fs"

open System.Net
open System.Diagnostics

let Run(req: HttpRequestMessage) : Task<HttpResponseMessage>  = 
    async {
        let response = Test().Response
        req.Properties.["LoadedScriptResponse"] <- response

        return new HttpResponseMessage(HttpStatusCode.OK, Content = new StringContent(response)) 
    } |> Async.StartAsTask
