#r "System.Runtime"
#r "System.Threading.Tasks"
#r "Microsoft.Azure.ApiHub.Sdk"

open System
open Microsoft.Azure.WebJobs.Host
open Microsoft.Azure.ApiHub

type SampleEntity = { Id : int; Text: string } 

let Run(text: string, client: ITableClient, log: TraceWriter) =
//    async {
//       let dataSet = client.GetDataSetReference()
//       let table = dataSet.GetTableReference<SampleEntity>("SampleTable")
//       let! ct = Async.CancellationToken
//       return! table.UpdateEntityAsync("1",{ Id = 1; Text = text }, ct) |> Async.AwaitTask
//    } |> Async.StartAsTask

       let dataSet = client.GetDataSetReference();
       let table = dataSet.GetTableReference<SampleEntity>("SampleTable")

       table.UpdateEntityAsync("1",{ Id = 1; Text = text }).Wait()
