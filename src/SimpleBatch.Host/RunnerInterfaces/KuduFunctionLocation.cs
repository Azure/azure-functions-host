using System;
using System.Reflection;
using Newtonsoft.Json;

namespace Microsoft.WindowsAzure.Jobs
{
    // Describe a function that lives behind a URL (such as with Kudu). 
    internal class KuduFunctionLocation : FunctionLocation, IUrlFunctionLocation
    {
        // Do a POST to this URI, and pass this instance in the body.  
        public string Uri { get; set; }

        public string AssemblyQualifiedTypeName { get; set; }
        public string MethodName { get; set; }

        // $$$ Needed for ICall support. 
        public override FunctionLocation ResolveFunctionLocation(string methodName)
        {
            return base.ResolveFunctionLocation(methodName);
        }

        public MethodInfoFunctionLocation Convert()
        {
            Type t = Type.GetType(AssemblyQualifiedTypeName);
            MethodInfo m = t.GetMethod(MethodName, BindingFlags.Public | BindingFlags.Static);
            return new MethodInfoFunctionLocation(m)
            {
                AccountConnectionString = this.AccountConnectionString,
            };
        }

        public override string GetId()
        {
            // Need  a URL so that different domains get their own namespace of functions. 
            // But also has to be a valid row/partition key since it's called by FunctionInvokeRequest.ToString()
            string accountName = Utility.GetAccountName(this.AccountConnectionString);
            string shortUrl = new Uri(this.Uri).Host; // gets a human readable (almost) unique name, without invalid chars
            return string.Format(@"kudu\{0}\{1}\{2}", shortUrl, accountName, MethodName);
        }

        public override string GetShortName()
        {
            return MethodName;
        }

        [JsonIgnore]
        public string InvokeUrl
        {
            get
            {
                return Uri;
            }
        }
    }
}
