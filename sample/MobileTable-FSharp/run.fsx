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
