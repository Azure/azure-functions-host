#r "TestDependency.dll"
#r "System.Threading.Tasks"
#r "System.Net.Http"

open System
open System.Net
open System.Net.Http
open TestDependency

let Run(req: HttpRequestMessage, log: TraceWriter) =
    let response = (new TestClass()).GetValue();
    new HttpResponseMessage(HttpStatusCode.OK, Content = new StringContent(response))   

