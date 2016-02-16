using System;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.Azure.WebJobs.Host;

public static void Run(string id, TraceWriter log)
{
    log.Verbose(string.Format("CSharp Queue trigger function processed a request. Name={0}", "tmp"));
}