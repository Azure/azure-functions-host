using System;
using System.Collections.Generic;
using System.Configuration;
using System.IO;
using System.Reflection;
using System.Threading;

namespace Microsoft.WindowsAzure.Jobs
{
    // Internal test hooks for the host.
    internal class JobHostTestHooks
    {
        // Don't validate the storage accounts passed into the host object.
        // This means the test can set the account to null if the operation truly should not need storage (such as just testing indexing)
        // or it can set it to developer storage if it's just using supported operations. 
        public bool SkipStorageValidation { get; set; }


        // If != null, then only index methods on this type, and don't reflect over all the assemblies. 
        public Type TypeToIndex { get; set; }
    }
}