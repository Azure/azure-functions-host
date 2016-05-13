#r "..\SharedAssemblies\PrimaryDependency.dll"

using System;
using PrimaryDependency;

public static void Run(HttpRequestMessage req, TraceWriter log)
{
    req.Properties["DependencyOutput"] = new Primary().GetValue();
}