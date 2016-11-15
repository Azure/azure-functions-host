open System
open Microsoft.Azure.WebJobs.Host

[<CLIMutable>]
type TestObject = 
    { SenderName : string
      Greeting : string }

let Run (req: TestObject, log: TraceWriter) =
    { req with Greeting = sprintf "Hello, %s" req.SenderName }
