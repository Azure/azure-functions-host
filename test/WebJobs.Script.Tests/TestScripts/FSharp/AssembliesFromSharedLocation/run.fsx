//----------------------------------------------------------------------------------------
// This prelude allows scripts to be edited in Visual Studio or another F# editing environment 

#if !COMPILED
#I "../../../../../bin/Binaries/WebJobs.Script.Host"
#r "Microsoft.Azure.WebJobs.Host.dll"
#r "Microsoft.Azure.WebJobs.Extensions.dll"
#endif

//----------------------------------------------------------------------------------------
// This is the implementation of the function 

#r "../SharedAssemblies/PrimaryDependency.dll"
#r "System.Threading.Tasks"
#r "System.Net.Http"

open System
open System.Net.Http
open System.Runtime.InteropServices
open Microsoft.Azure.WebJobs.Host
open PrimaryDependency



let Run(req: HttpRequestMessage, log: TraceWriter) =
    req.Properties.["DependencyOutput"] <- (new Primary()).GetValue();
