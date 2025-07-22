// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Microsoft.Azure.WebJobs.Script.Diagnostics.Extensions;
using Microsoft.Extensions.Logging;

namespace Microsoft.Azure.WebJobs.Script.Host
{
    internal sealed class MissingAzureFunctionsFolderValidator : IFunctionAppValidator
    {
        public void Validate(ScriptJobHostOptions options, IEnvironment environment, ILogger logger)
        {
            string azureFunctionsDirPath = Path.Combine(options.RootScriptPath, ScriptConstants.AzureFunctionsSystemDirectoryName);

            if (options.RootScriptPath is not null &&
                !options.IsDefaultHostConfig &&
                Utility.IsDotnetIsolatedApp(environment: environment) &&
                !Directory.Exists(azureFunctionsDirPath))
            {
                IEnumerable<string> azureFunctionsDirectories = Directory.GetDirectories(options.RootScriptPath, ScriptConstants.AzureFunctionsSystemDirectoryName, SearchOption.AllDirectories)
                    .Where(dir => !dir.Equals(azureFunctionsDirPath, StringComparison.OrdinalIgnoreCase));

                if (azureFunctionsDirectories.Any())
                {
                    string azureFunctionsDirectoriesPath = string.Join(", ", azureFunctionsDirectories).Replace(options.RootScriptPath, string.Empty);
                    logger.IncorrectAzureFunctionsFolderPath(azureFunctionsDirectoriesPath);
                }
                else
                {
                    logger.MissingAzureFunctionsFolder();
                }
            }
        }
    }
}
