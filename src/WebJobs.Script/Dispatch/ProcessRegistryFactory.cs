// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System.Runtime.InteropServices;
using Microsoft.Azure.WebJobs.Script.Config;

namespace Microsoft.Azure.WebJobs.Script.Dispatch
{
    internal class ProcessRegistryFactory
    {
        static internal IProcessRegistry Create()
        {
            if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows)
                && !ScriptSettingsManager.Instance.IsAzureEnvironment)
            {
                return new JobObjectRegistry();
            }
            else
            {
                return new EmptyProcessRegistry();
            }
        }
    }
}
