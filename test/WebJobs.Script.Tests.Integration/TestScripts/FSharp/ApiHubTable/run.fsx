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


