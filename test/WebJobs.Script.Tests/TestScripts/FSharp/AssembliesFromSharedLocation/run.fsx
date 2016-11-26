#r "../SharedAssemblies/PrimaryDependency.dll"
#r "System.Threading.Tasks"
#r "System.Net.Http"

open System
open System.Net.Http
open Microsoft.Azure.WebJobs.Host
open PrimaryDependency



let Run(req: HttpRequestMessage, log: TraceWriter) =
    req.Properties.["DependencyOutput"] <- (new Primary()).GetValue();
