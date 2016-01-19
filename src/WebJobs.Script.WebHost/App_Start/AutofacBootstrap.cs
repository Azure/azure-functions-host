using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using System.Web.Hosting;
using Autofac;
using Microsoft.Azure.WebJobs.Host;
using Microsoft.Azure.WebJobs.Script;

namespace WebJobs.Script.WebHost.App_Start
{
    public class AutofacBootstrap
    {
        internal static void Initialize(ContainerBuilder builder)
        {
            string logFilePath;
            string scriptRootPath;
            string home = Environment.GetEnvironmentVariable("HOME");
            if (!string.IsNullOrEmpty(home))
            {
                // we're running in Azure
                scriptRootPath = Path.Combine(home, @"site\wwwroot");
                logFilePath = Path.Combine(home, @"LogFiles\Application\Functions");
            }
            else
            {
                // we're running locally
                scriptRootPath = Path.Combine(HostingEnvironment.ApplicationPhysicalPath, @"..\..\sample");
                logFilePath = Path.Combine(Path.GetTempPath(), @"Functions");
            }

            TraceWriter traceWriter = new WebTraceWriter(logFilePath);
            builder.RegisterInstance<TraceWriter>(traceWriter);

            ScriptHostConfiguration scriptHostConfig = new ScriptHostConfiguration()
            {
                RootPath = scriptRootPath
            };
            WebScriptHostManager scriptHostManager = new WebScriptHostManager(scriptHostConfig, traceWriter);
            builder.RegisterInstance<WebScriptHostManager>(scriptHostManager);

            // When using Task.Run, We're getting occasional memory AccessViolationException
            // exceptions from Edge.js. Could this be an IISExpress issue only?
            Task.Run(() => scriptHostManager.StartAsync(CancellationToken.None));
        }
    }
}