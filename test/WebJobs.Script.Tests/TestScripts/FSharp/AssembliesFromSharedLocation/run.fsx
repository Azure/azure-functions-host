#r "..\SharedAssemblies\PrimaryDependency.dll"

open System
open PrimaryDependency

let Run(req: HttpRequestMessage, log: TraceWriter) =
    req.Properties.["DependencyOutput"] <- new Primary().GetValue();
