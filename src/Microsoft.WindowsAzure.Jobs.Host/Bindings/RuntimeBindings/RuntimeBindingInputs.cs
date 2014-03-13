using System.Collections.Generic;

namespace Microsoft.WindowsAzure.Jobs
{
    // Inputs that can impact producing a runtime binding.     
    internal class RuntimeBindingInputs : IRuntimeBindingInputs
    {
        private FunctionLocation _location;

        public RuntimeBindingInputs(string accountConnectionString)
        {
            this.AccountConnectionString = accountConnectionString;
        }

        // Location lets us get the account string and pull down any extra config files. 
        public RuntimeBindingInputs(FunctionLocation location)
            : this(location.AccountConnectionString)
        {
            this._location = location;
        }

        // Account that binding is relative too. 
        // public CloudStorageAccount _account;
        public string AccountConnectionString { get; private set; }

        public IDictionary<string, string> NameParameters { get; set; }
    }
}
