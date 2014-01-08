using System;
using System.Collections.Generic;
using System.Configuration;
using System.IO;
using System.Reflection;
using System.Threading;

namespace Microsoft.WindowsAzure.Jobs
{
    // Service for validating that storage account connection strings. 
    // (they parse, the accounts are valid, credentials work, etc)
    internal interface IStorageValidator
    {
        // User account may be mandatory, logging may be optional. 
        void Validate(string dataConnectionString, string runtimeConnectionString);
    }
}
