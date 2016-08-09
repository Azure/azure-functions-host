//----------------------------------------------------------------------------------------
// This prelude allows scripts to be edited in Visual Studio or another F# editing environment 

#if !COMPILED
open System.Runtime.InteropServices
#I "../../../../../bin/Binaries/WebJobs.Script.Host"
#r "Microsoft.Azure.WebJobs.Host.dll"
#r "Microsoft.Azure.WebJobs.Extensions.dll"
#endif

//----------------------------------------------------------------------------------------
// This is the implementation of the function 

open System
open Microsoft.Azure.WebJobs.Host

[<CLIMutable>]
type Item = 
    { Id : string
      Text : string
      IsProcessed : bool
      ProcessedAt : DateTimeOffset  
      // Mobile table properties
      CreatedAt : DateTimeOffset  }

let Run(input: string, [<Out>] item: byref<Item>, log: TraceWriter) =
    item <-
        { Id = input
          Text = "Hello from F#!"
          IsProcessed = false
          ProcessedAt = DateTimeOffset()
          CreatedAt = DateTimeOffset() }

    log.Info(sprintf "Inserting item %s" item.Id)


