using System;
using System.Diagnostics;
using System.Fabric;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.ServiceFabric.Services.Runtime;
using Microsoft.Diagnostics.EventFlow.ServiceFabric;

namespace WebJobs.Script.FabricService
{
    internal static class Program
    {
        /// <summary>
        /// This is the entry point of the service host process.
        /// </summary>
        private static void Main()
        {
            try
            {
                // **** Instantiate log collection via EventFlow
                using (var diagnosticsPipeline = ServiceFabricDiagnosticPipelineFactory.CreatePipeline("Microsoft-WebJobs.Script.FabricHost-FabricService"))
                {

                    ServiceRuntime.RegisterServiceAsync("FabricServiceType",
                        context => new FabricService(context)).GetAwaiter().GetResult();

                    ServiceEventSource.Current.ServiceTypeRegistered(Process.GetCurrentProcess().Id, typeof(FabricService).Name);

                    Thread.Sleep(Timeout.Infinite);
                }
            }
            catch (Exception e)
            {
                ServiceEventSource.Current.ServiceHostInitializationFailed(e.ToString());
                throw;
            }
        }
    }
}
