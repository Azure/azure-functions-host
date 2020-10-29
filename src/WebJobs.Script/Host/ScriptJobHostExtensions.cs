// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.Azure.WebJobs.Script.Description;

namespace Microsoft.Azure.WebJobs.Script
{
    public static class ScriptJobHostExtensions
    {
        private const string WarmupFunctionName = "Warmup";
        private const string WarmupTriggerName = "WarmupTrigger";

        /// <summary>
        /// Lookup a function by name
        /// </summary>
        /// <param name="name">name of function</param>
        /// <returns>function or null if not found</returns>
        public static FunctionDescriptor GetFunctionOrNull(this IScriptJobHost scriptJobHost, string name)
        {
            return scriptJobHost.Functions.FirstOrDefault(f => IsFunctionNameMatch(f.Name, name));
        }

        private static bool IsFunctionNameMatch(string functionName, string comparison)
        {
            return string.Equals(functionName, comparison, StringComparison.OrdinalIgnoreCase);
        }

        /// <summary>
        /// Checks if a function is a warmup function
        /// </summary>
        /// <returns>true if a warmup function or else false</returns>
        public static bool IsWarmupFunction(this FunctionDescriptor function)
        {
            if (IsFunctionNameMatch(function.Name, WarmupFunctionName))
            {
                return function.Metadata
                    .InputBindings
                    .Any(b => b.IsTrigger && b.Type.Equals(WarmupTriggerName, StringComparison.OrdinalIgnoreCase));
            }

            return false;
        }
    }
}
