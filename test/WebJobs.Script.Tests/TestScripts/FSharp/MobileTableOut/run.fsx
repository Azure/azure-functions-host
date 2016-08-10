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
open Microsoft.Azure.WebJobs.Host

type Item() = 
    member val Id = "" with get,set
    member val Text = "" with get,set
    member val IsProcessed = false with get,set
    member val ProcessedAt = DateTimeOffset() with get,set
    // Mobile table properties
    member val CreatedAt = DateTimeOffset() with get,set

let Run(input: string, item: byref<Item>, log: TraceWriter) =
    item <-
        Item(Id = input, 
             Text = "This was updated!",
             IsProcessed = false,
             ProcessedAt = DateTimeOffset(),
             CreatedAt = DateTimeOffset())

    log.Info(sprintf "Inserting item %s" item.Id)


