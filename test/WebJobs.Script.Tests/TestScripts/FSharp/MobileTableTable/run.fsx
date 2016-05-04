#r "System.Threading.Tasks"

open System
open System.Threading.Tasks
open Microsoft.Azure.WebJobs.Host
open Microsoft.WindowsAzure.MobileServices

type Item = 
    { mutable Id : string
      mutable Text : string
      mutable IsProcessed : bool
      mutable ProcessedAt : DateTimeOffset  
      // Mobile table properties
      mutable CreatedAt : DateTimeOffset  }


let Run(input: string , table: IMobileServiceTable<Item>) = 
    async { 
        let item =
            { Id = input
              Text = "Hello from F#!"
              IsProcessed = false
              ProcessedAt = DateTimeOffset.Now
              CreatedAt = DateTimeOffset.Now }
        do! table.InsertAsync item |> Async.AwaitTask
        return ()
    } |> Async.StartAsTask

