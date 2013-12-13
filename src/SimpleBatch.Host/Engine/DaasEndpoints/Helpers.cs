using System;
using System.Runtime.CompilerServices;
using Microsoft.WindowsAzure;
using RunnerInterfaces;

namespace DaasEndpoints
{
    internal static class Helpers
    {
        // Queue execution for any blobs in the given path
        // conatiner\blob1\blobsubdir
        // Returns count scanned
        public static int ScanBlobDir(Services services, CloudStorageAccount account, CloudBlobPath path)
        {
            // $$$ Need to determine FunctionDefinition from the given blob. 
            throw new NotImplementedException();
        }
    }

    internal static class AzureRuntime
    {
        [MethodImpl(MethodImplOptions.NoInlining)]
        public static string GetConfigurationSettingValue(string name)
        {
            throw new InvalidOperationException("No azure runtime");
        }

        public static bool IsAvailable
        {
            get
            {
                return false;
            }
        }
    }
}

