using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using Executor;
using Microsoft.WindowsAzure;
using Microsoft.WindowsAzure.StorageClient;
using RunnerInterfaces;

namespace Orchestrator
{
    // Settings for azure services
    // Also has helper methods for wrapping calls to other azure services.
    public interface IOrchestratorSettings
    {
        FunctionIndexEntity[] ReadFunctionTable();        
        void QueueFunction(FunctionInstance instance);        
    }
}