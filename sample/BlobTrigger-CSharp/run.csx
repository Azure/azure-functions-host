using System;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.Azure.WebJobs.Host;

public static void Run(string id, TraceWriter log)
{
    log.Verbose(string.Format("CSharp Blob trigger function processed a request. Item Id={0}", id));
}