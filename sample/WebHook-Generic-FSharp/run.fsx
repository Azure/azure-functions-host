//----------------------------------------------------------------------------------------
// This prelude allows scripts to be edited in Visual Studio or another F# editing environment 

#if !COMPILED
#I "../../bin/Binaries/WebJobs.Script.Host"
#r "Microsoft.Azure.WebJobs.Host.dll"
#r "Microsoft.Azure.WebJobs.Extensions.dll"
#r "Newtonsoft.Json"
#endif

//----------------------------------------------------------------------------------------
// This is the implementation of the function 

open System
open System.Net
open Newtonsoft.Json
open Newtonsoft.Json.Linq

[<CLIMutable>]
type Payload =
    { Id : string
      Value : string }

let Run (payload: Payload) : JObject =
    let body = new JObject()
    body.Add("result", JToken.op_Implicit (sprintf "Value: %s" payload.Value))
    body

