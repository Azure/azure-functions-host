using System.Collections.Generic;

namespace Microsoft.Azure.Jobs
{
    // Inputs that can impact producing a runtime binding.     
    internal class RuntimeBindingInputs : IRuntimeBindingInputs
    {
        private FunctionLocation _location;

        public RuntimeBindingInputs(string storageConnectionString)
            :this(storageConnectionString, null)
        {
        }

        public RuntimeBindingInputs(string accountConnectionString, string serviceBusConnectionString)
        {
            this.StorageConnectionString = accountConnectionString;
            this.ServiceBusConnectionString = serviceBusConnectionString;
        }

        // Location lets us get the account string and pull down any extra config files. 

        public RuntimeBindingInputs(FunctionLocation location)
            : this(location.StorageConnectionString, location.ServiceBusConnectionString)
        {
            this._location = location;
        }

        public string ServiceBusConnectionString { get; private set; }

        public string StorageConnectionString { get; private set; }

        public IDictionary<string, string> NameParameters { get; set; }
    }
}
