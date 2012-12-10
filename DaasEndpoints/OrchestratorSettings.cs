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
using DaasEndpoints;

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
        Services _services;

        public OrchestratorSettings(Services services)
        {
            _services = services;
        }

        public FunctionIndexEntity[] ReadFunctionTable()
        {
            var funcs = _services.GetFunctions();
            return funcs;
        }

        public void QueueFunction(FunctionInvokeRequest instance)
        {
            _services.QueueExecutionRequest(instance);
        }      
    }
}