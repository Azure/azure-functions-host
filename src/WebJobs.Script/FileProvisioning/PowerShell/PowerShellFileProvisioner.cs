// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System;
using System.IO;
using System.Threading.Tasks;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace Microsoft.Azure.WebJobs.Script.FileProvisioning.PowerShell
{
    internal class PowerShellFileProvisioner : IFuncAppFileProvisioner
    {
        /// <summary>
        /// Adds the required files to the function app
        /// </summary>
        /// <param name="scriptRootPath">The root path of the function app</param>
        /// <returns>An empty completed task <see cref="Task"/></returns>
        public Task ProvisionFiles(string scriptRootPath)
        {
            if (string.IsNullOrWhiteSpace(scriptRootPath))
            {
                throw new ArgumentException("The parameter {0} cannot be null or empty", nameof(scriptRootPath));
            }

            AddRequirementsFile(scriptRootPath);
            AddProfileFile(scriptRootPath);
            return Task.CompletedTask;
        }

        private void AddRequirementsFile(string scriptRootPath)
        {
            string requirementsFilePath = Path.Combine(scriptRootPath, "requirements.psd1");
            if (!File.Exists(requirementsFilePath))
            {
                string content = FileUtility.ReadResourceString($"Microsoft.Azure.WebJobs.Script.FileProvisioning.PowerShell.requirements.psd1");
                File.WriteAllText(requirementsFilePath, content);
            }
        }

        private void AddProfileFile(string scriptRootPath)
        {
            string profileFilePath = Path.Combine(scriptRootPath, "profile.ps1");
            if (!File.Exists(profileFilePath))
            {
                string content = FileUtility.ReadResourceString($"Microsoft.Azure.WebJobs.Script.FileProvisioning.PowerShell.profile.ps1");
                File.WriteAllText(profileFilePath, content);
            }
        }
    }
}
