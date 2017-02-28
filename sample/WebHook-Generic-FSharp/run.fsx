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

