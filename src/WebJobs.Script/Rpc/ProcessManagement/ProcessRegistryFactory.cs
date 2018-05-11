// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System.Runtime.InteropServices;
using Microsoft.Azure.WebJobs.Script.Config;

namespace Microsoft.Azure.WebJobs.Script.Rpc
{
    internal class ProcessRegistryFactory
    {
        internal static IProcessRegistry Create()
        {
            // W3WP already manages job objects
            if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows)
                && !ScriptSettingsManager.Instance.IsAppServiceEnvironment)
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
