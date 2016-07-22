//----------------------------------------------------------------------------------------
// This prelude allows scripts to be edited in Visual Studio or another F# editing environment 

#if !COMPILED
#I "../../bin/Binaries/WebJobs.Script.Host"
#r "Microsoft.Azure.WebJobs.Host.dll"
#endif

//----------------------------------------------------------------------------------------
// This is the implementation of the function 

open System
open Microsoft.Azure.WebJobs.Host

let Run (input: string, messageProperties: byref<string>, log: TraceWriter) =

    let json = """{"message":"Hello from F# ! ","location":"fsharp.org"}"""

    messageProperties <- json
