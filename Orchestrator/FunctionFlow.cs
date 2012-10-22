using System;
using System.Collections.Generic;
using System.Data.Services.Client;
using System.Data.Services.Common;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Threading;
using Microsoft.WindowsAzure;
using Microsoft.WindowsAzure.StorageClient;
using Newtonsoft.Json;
using RunnerInterfaces;

namespace Orchestrator
{
    // Describes the input and output flows to a function.  
    // This can be determined from a function by analyzing the declarative attributes, 
    // or possible via a config file. 
    // Orchestrator uses this to know 
    // 1) when to invoke functions; 
    // 2) how to bind the parameters
    // This should be serializable, it will get persisted in azure tables.
    public class FunctionFlow
    {
        // Azure Client library bug: requires at least one trivial property. 
        // Else it crashes before even getting to the custom write hooks. 
        public string Dummy { get; set; }

        // Should all be non-null, else we have error!
        public ParameterStaticBinding[] Bindings { get; set; }

        // Collect all input parameters {name} across all bindings.
        public IEnumerable<string> GetInputParameters()
        {
            HashSet<string> names = new HashSet<string>();

            foreach (var flow in this.Bindings)
            {
                names.UnionWith(flow.ProducedRouteParameters);
            }
            return names;
        }
    }
}