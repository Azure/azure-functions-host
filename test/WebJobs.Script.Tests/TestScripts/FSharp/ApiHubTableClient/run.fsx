#r "Microsoft.Azure.ApiHub.Sdk"

open System
open Microsoft.Azure.ApiHub

type SampleEntity = { Id : int; Text: string } 

let Run(text: string , client: ITableClient , log: TraceWriter ) =
    async {
       let dataSet = client.GetDataSetReference();
       let table = dataSet.GetTableReference<SampleEntity>("SampleTable");

       return! table.UpdateEntityAsync("1",{ Id = 1; Text = text }) |> Async.AwaitTask
    } |> Async.StartAsTask
