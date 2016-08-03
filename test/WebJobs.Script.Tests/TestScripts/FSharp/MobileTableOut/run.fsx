//----------------------------------------------------------------------------------------
// This prelude allows scripts to be edited in Visual Studio or another F# editing environment 

#if !COMPILED
#I "../../../../../bin/Binaries/WebJobs.Script.Host"
#r "Microsoft.Azure.WebJobs.Host.dll"
#r "Microsoft.Azure.WebJobs.Extensions.dll"
#endif

//----------------------------------------------------------------------------------------
// This is the implementation of the function 

open System
open System.Runtime.InteropServices
open Microsoft.Azure.WebJobs.Host

type Item = 
    { mutable Id : string
      mutable Text : string
      mutable IsProcessed : bool
      mutable ProcessedAt : DateTimeOffset  
      // Mobile table properties
      mutable CreatedAt : DateTimeOffset  }

let Run(input: string, [<Out>] item: byref<Item>, log: TraceWriter) =
    item <-
        { Id = input
          Text = "Hello from F#!"
          IsProcessed = false
          ProcessedAt = DateTimeOffset.Now
          CreatedAt = DateTimeOffset.Now }

    log.Info(sprintf "Inserting item %s" item.Id)


