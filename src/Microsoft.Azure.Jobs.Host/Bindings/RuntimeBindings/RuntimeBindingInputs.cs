using System.Collections.Generic;

namespace Microsoft.Azure.Jobs
{
    // Inputs that can impact producing a runtime binding.     
    internal class RuntimeBindingInputs : IRuntimeBindingInputs
    {
        private FunctionLocation _location;

        public RuntimeBindingInputs(string storageConnectionString)
        {
            this.StorageConnectionString = storageConnectionString;
        }

        // Location lets us get the account string and pull down any extra config files. 

        public RuntimeBindingInputs(FunctionLocation location)
            : this(location.StorageConnectionString)
        {
            this._location = location;
        }

        public string StorageConnectionString { get; private set; }

        public IDictionary<string, string> NameParameters { get; set; }
    }
}
