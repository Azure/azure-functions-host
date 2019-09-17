// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System;
using Microsoft.Azure.WebJobs.Script.FileProvisioning.PowerShell;
using Microsoft.Extensions.Logging;

namespace Microsoft.Azure.WebJobs.Script.FileProvisioning
{
    internal class FuncAppFileProvisionerFactory : IFuncAppFileProvisionerFactory
    {
        private readonly ILoggerFactory _loggerFactory;

        public FuncAppFileProvisionerFactory(ILoggerFactory loggerFactory)
        {
            _loggerFactory = loggerFactory;
        }

        public IFuncAppFileProvisioner CreatFileProvisioner(string runtime)
        {
            if (string.IsNullOrWhiteSpace(runtime))
            {
                return null;
            }

            switch (runtime.ToLowerInvariant())
            {
                case "powershell":
                    return new PowerShellFileProvisioner(_loggerFactory);
                default:
                    return null;
            }
        }
    }
}
