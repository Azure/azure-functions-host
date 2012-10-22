using System;
using System.Collections.Generic;
using System.IO;
using Executor;
using Microsoft.WindowsAzure.StorageClient;
using Orchestrator;
using RunnerInterfaces;
using System.Linq;
using Newtonsoft.Json;
using System.Threading;
using Microsoft.WindowsAzure;

// Despite the name, this is not an IOC container. 
// This provides a global view of the distributed application (service, webpage, logging, tooling, etc)
// Anything that needs an azure endpoint can go here.
// This access the raw settings (especially account name) from Secrets, but then also provides the 
// policy and references to stitch everything together. 
namespace Orchestrator
{
    // Settings that bind against real cloud services.
    public class OrchestratorSettings : IOrchestratorSettings
    {
        public FunctionIndexEntity[] ReadFunctionTable()
        {
            var funcs = Services.GetFunctions();
            return funcs;
        }

        public void QueueFunction(FunctionInstance instance)
        {
            Services.QueueExecutionRequest(instance);
        }      
    }
}