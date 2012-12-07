using System;
using Microsoft.WindowsAzure;
using RunnerHost;
using RunnerInterfaces;

namespace Orchestrator
{
    // Represents binding to a named parameter from the named parameter dictionary. 
    // Dictionary could be populated via route parameters in a Blob pattern match, explicitly by user, or somewhere else.
    public class NameParameterStaticBinding : ParameterStaticBinding
    {
        public string KeyName { get; set; }
        public bool UserSupplied { get; set; } 

        public override ParameterRuntimeBinding Bind(RuntimeBindingInputs inputs)
        {
            string value;
            if (inputs._nameParameters != null)
            {
                if (inputs._nameParameters.TryGetValue(KeyName, out value))
                {
                    return new LiteralStringParameterRuntimeBinding { Value = value };
                }
            }
            if (UserSupplied)
            {
                // Not found. Do late time binding. 
                return new UnknownParameterRuntimeBinding { AccountConnectionString = Utility.GetConnectionString(inputs._account) };
            }
            throw new InvalidOperationException(string.Format("Can't bind keyname '{0}'", KeyName));            
        }

        public override ParameterRuntimeBinding BindFromInvokeString(CloudStorageAccount account, string invokeString)
        {
            if (string.IsNullOrWhiteSpace(invokeString) && UserSupplied)
            {
                // Not found. Do late time binding. 
                return new UnknownParameterRuntimeBinding { AccountConnectionString = Utility.GetConnectionString(account) };
            }
            return new LiteralStringParameterRuntimeBinding { Value = invokeString };
        }

        public override string Description
        {
            get
            {
                if (UserSupplied)
                {
                    return "model bound.";
                }
                return string.Format("mapped from keyname '{0}'", "{" + KeyName + "}");
            }
        }

        public override TriggerType GetTriggerType()
        {
            return TriggerType.Ignore; // Constants
        }
    }
}