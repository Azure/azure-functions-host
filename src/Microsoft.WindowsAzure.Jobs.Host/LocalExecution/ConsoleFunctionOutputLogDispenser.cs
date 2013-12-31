using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Reflection;
using Microsoft.WindowsAzure.Jobs.Internals;
using Microsoft.WindowsAzure.StorageClient;
using AzureTables;

namespace Microsoft.WindowsAzure.Jobs
{
    // Implementation of IFunctionOuputLogDispenser that just logs to the console. 
    class ConsoleFunctionOuputLogDispenser : IFunctionOuputLogDispenser
    {
        public FunctionOutputLog CreateLogStream(FunctionInvokeRequest request)
        {
            return new FunctionOutputLog();
        }
    }
}