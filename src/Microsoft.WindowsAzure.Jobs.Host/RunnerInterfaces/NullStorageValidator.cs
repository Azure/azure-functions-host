using System;
using System.Collections.Generic;
using System.Configuration;
using System.IO;
using System.Reflection;
using System.Threading;

namespace Microsoft.WindowsAzure.Jobs
{
    // StorageValidator that skips validation. 
    internal class NullStorageValidator : IStorageValidator
    {
        public void Validate(string dataConnectionString, string runtimeConnectionString)
        {
            // nop
        }
    }

}
