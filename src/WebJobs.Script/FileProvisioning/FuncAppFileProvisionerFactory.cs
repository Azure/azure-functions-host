// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System;
using Microsoft.Azure.WebJobs.Script.FileProvisioning.PowerShell;

namespace Microsoft.Azure.WebJobs.Script.FileProvisioning
{
    internal class FuncAppFileProvisionerFactory : IFuncAppFileProvisionerFactory
    {
        public IFuncAppFileProvisioner CreatFileProvisioner(string runtime)
        {
            if (string.IsNullOrWhiteSpace(runtime))
            {
                return null;
            }

            switch (runtime.ToLowerInvariant())
            {
                case "powershell":
                    return new PowerShellFileProvisioner();
                default:
                    return null;
            }
        }
    }
}
