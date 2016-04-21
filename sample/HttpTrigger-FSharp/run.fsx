//----------------------------------------------------------------------------------------
// This prelude allows scripts to be edited in Visual Studio or another F# editing environment 

#if !COMPILED
#I @"../../bin/Binaries/WebJobs.Script.Host"
#r "Microsoft.Azure.WebJobs.Host.dll"
#r "System.Web.Http.dll"
#r "System.Net.Http.dll"
#endif

//----------------------------------------------------------------------------------------
// This is the body of the function 

open System
open System.Linq
open System.Net
open System.Net.Http
open System.Threading.Tasks
open Microsoft.Azure.WebJobs.Host

let Run (req: HttpRequestMessage , log: TraceWriter) : Task<HttpResponseMessage> =
    async { 
        let queryParams = req.GetQueryNameValuePairs().ToDictionary((fun p -> p.Key), (fun p -> p.Value), StringComparer.OrdinalIgnoreCase)

        log.Verbose(sprintf "F# HTTP trigger function processed a request. Name=%A" req.RequestUri)

        let res =
            match queryParams.TryGetValue("name") with 
            | true, name -> 
                new HttpResponseMessage(HttpStatusCode.OK, Content = new StringContent("Hello " + name))
            | _ -> 
                new HttpResponseMessage(HttpStatusCode.BadRequest, Content = new StringContent("Please pass a name on the query string"))

        return res
    } |> Async.StartAsTask


