#r "../SharedAssemblies/PrimaryDependency.dll"
#r "System.Threading.Tasks"
#r "System.Net.Http"

open System
open System.Net
open System.Net.Http
open Microsoft.Azure.WebJobs.Host
open PrimaryDependency

let Run(req: HttpRequestMessage, log: TraceWriter) =
    let response = (new Primary()).GetValue()
    new HttpResponseMessage(HttpStatusCode.OK, Content = new StringContent(response))   
