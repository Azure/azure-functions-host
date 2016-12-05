#r "TestDependency.dll"
#r "System.Threading.Tasks"
#r "System.Net.Http"

open System
open System.Net.Http
open TestDependency



let Run(req: HttpRequestMessage, log: TraceWriter) =
    req.Properties.["DependencyOutput"] <- (new TestClass()).GetValue();
