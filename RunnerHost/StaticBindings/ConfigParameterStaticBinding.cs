using System;
using System.Collections.Generic;
using System.IO;
using Microsoft.WindowsAzure;
using RunnerHost;
using RunnerInterfaces;

namespace Orchestrator
{
    public class ConfigParameterStaticBinding : ParameterStaticBinding
    {
        // Filename that the config is coming from 
        public string Filename { get; set; }
        
        public override ParameterRuntimeBinding Bind(RuntimeBindingInputs inputs)
        {   
            // Capture the config at the time the function is bound. 
            // This means that if we change the config and replay the function, it uses the original config. 
            // This makes replay more faithful. 
            string json = inputs.ReadFile(this.Filename);

            return new LiteralObjectParameterRuntimeBinding { LiteralJson = json };
        }

        public override ParameterRuntimeBinding BindFromInvokeString(CloudStorageAccount account, string invokeString)
        {
            // This should round trip with the ParameterRuntimeBinding.ConvertToInvokeString, 
            // which returns literal json. 
            string json = invokeString;
            return new LiteralObjectParameterRuntimeBinding { LiteralJson = json };
        }

        public override string Description
        {
            get 
            { 
                return string.Format("Config file: {0}", this.Filename); 
            }
        }
    }
}
