// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System;
using System.Collections.Generic;
using System.Text;
using Microsoft.Extensions.Hosting;

namespace Microsoft.Azure.WebJobs.Script
{
    public static class ScriptJobHostEnvironmentExtensions
    {
        public static bool IsDevelopment(this IScriptJobHostEnvironment environment)
        {
            if (environment == null)
            {
                throw new ArgumentNullException(nameof(environment));
            }

            return environment.IsEnvironment(EnvironmentName.Development);
        }

        public static bool IsEnvironment(this IScriptJobHostEnvironment environment, string environmentName)
        {
            if (environment == null)
            {
                throw new ArgumentNullException(nameof(environment));
            }

            return string.Equals(environment.EnvironmentName, environmentName, StringComparison.OrdinalIgnoreCase);
        }
    }
}
