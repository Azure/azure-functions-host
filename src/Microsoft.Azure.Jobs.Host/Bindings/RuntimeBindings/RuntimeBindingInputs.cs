using System.Collections.Generic;

namespace Microsoft.Azure.Jobs
{
    // Inputs that can impact producing a runtime binding.     
    internal class RuntimeBindingInputs : IRuntimeBindingInputs
    {
        private FunctionLocation _location;

        public RuntimeBindingInputs(string accountConnectionString)
            :this(accountConnectionString, null)
        {
        }

        public RuntimeBindingInputs(string accountConnectionString, string serviceBusConnectionString)
        {
            this.AccountConnectionString = accountConnectionString;
            this.ServiceBusConnectionString = serviceBusConnectionString;
        }

        // Location lets us get the account string and pull down any extra config files. 

        public RuntimeBindingInputs(FunctionLocation location)
            : this(location.AccountConnectionString, location.ServiceBusConnectionString)
        {
            this._location = location;
        }

        public string ServiceBusConnectionString { get; private set; }

        public string AccountConnectionString { get; private set; }

        public IDictionary<string, string> NameParameters { get; set; }
    }
}
