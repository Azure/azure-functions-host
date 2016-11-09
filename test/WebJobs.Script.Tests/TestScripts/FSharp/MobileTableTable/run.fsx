//----------------------------------------------------------------------------------------
// This prelude allows scripts to be edited in Visual Studio or another F# editing environment 

#if !COMPILED
#I "../../../../../src/WebJobs.Script.Host/bin/Debug"
#r "Microsoft.Azure.WebJobs.Host.dll"
#r "Microsoft.Azure.WebJobs.Extensions.dll"
#endif

//----------------------------------------------------------------------------------------
// This is the implementation of the function 

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

