using System;
using System.Runtime.CompilerServices;

namespace Microsoft.WindowsAzure.Jobs
{
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
