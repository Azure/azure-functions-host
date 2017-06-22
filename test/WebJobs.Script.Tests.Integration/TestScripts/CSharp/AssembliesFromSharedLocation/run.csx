#r "..\SharedAssemblies\PrimaryDependency.dll"

using PrimaryDependency;

public static void Run(HttpRequest req, TraceWriter log)
{
    req.HttpContext.Items["DependencyOutput"] = new Primary().GetValue();
}