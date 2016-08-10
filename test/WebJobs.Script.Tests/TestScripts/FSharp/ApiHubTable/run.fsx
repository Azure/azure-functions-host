//----------------------------------------------------------------------------------------
// This prelude allows scripts to be edited in Visual Studio or another F# editing environment 

#if !COMPILED
#I "../../../../../bin/Binaries/WebJobs.Script.Host"
#r "Microsoft.Azure.WebJobs.Host.dll"
#r "Microsoft.Azure.WebJobs.Extensions.dll"
#endif

//----------------------------------------------------------------------------------------
// This is the implementation of the function 

#r "Microsoft.Azure.ApiHub.Sdk"

open System
open Microsoft.Azure.WebJobs.Host
open Microsoft.Azure.ApiHub

[<CLIMutable>]
type TestInput = { Id : int; Value: string }

[<CLIMutable>]
type SampleEntity = { Id : int; Text: string }

let Run(input: TestInput, table: ITable<SampleEntity>, log: TraceWriter) =
    async { 
        let! ct = Async.CancellationToken
        return! table.UpdateEntityAsync(input.Id.ToString(), { Id = input.Id; Text = input.Value }, ct) |> Async.AwaitTask
    } |> Async.StartAsTask


