#r "System.Threading.Tasks"
#r "Newtonsoft.Json"

#r "Microsoft.WindowsAzure.Mobile.dll"

open System
open System.Threading.Tasks
open Microsoft.Azure.WebJobs.Host
open Microsoft.WindowsAzure.MobileServices

[<CLIMutable>]
type Item = 
    { Id : string
      Text : string
      IsProcessed : bool
      ProcessedAt : DateTimeOffset  
      // Mobile table properties
      CreatedAt : DateTimeOffset  }


let Run(input: string , table: IMobileServiceTable<Item>) = 
    async { 
        let item =
            { Id = input
              Text = "Hello from F#!"
              IsProcessed = false
              ProcessedAt = DateTimeOffset.Now
              CreatedAt = DateTimeOffset.Now }
        do! table.InsertAsync item |> Async.AwaitTask
    } |> Async.StartAsTask

