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

type Item = 
    { Id : string
      Text : string
      IsProcessed : bool
      ProcessedAt : DateTimeOffset
      CreatedAt : DateTimeOffset }

let Run (input : string, log: TraceWriter) = 
    { Text = "Hello from F#! " + input
      Id = "cew02"
      IsProcessed = true
      ProcessedAt = System.DateTimeOffset.Now
      CreatedAt = System.DateTimeOffset.Now }
