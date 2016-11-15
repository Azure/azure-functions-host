open System
open Microsoft.Azure.WebJobs.Host

type Item() = 
    member val Id = "" with get,set
    member val Text = "" with get,set
    member val IsProcessed = false with get,set
    member val ProcessedAt = DateTimeOffset() with get,set
    // Mobile table properties
    member val CreatedAt = DateTimeOffset() with get,set

let Run(input: string, log: TraceWriter) =
    let item = Item(Id = input, Text = "Hello from F#!", IsProcessed = false, ProcessedAt = DateTimeOffset(), CreatedAt = DateTimeOffset())
    log.Info(sprintf "Inserting item %s" item.Id)
    item

    


