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
        /// Lookup a warmup function
        /// </summary>
        /// <returns>Warmup function or null if not found</returns>
        public static FunctionDescriptor GetWarmupFunctionOrNull(this IScriptJobHost scriptJobHost)
        {
            return scriptJobHost.Functions.FirstOrDefault(f =>
            {
                return IsFunctionNameMatch(f.Name, WarmupFunctionName)
                && f.Metadata
                    .InputBindings
                    .Any(b => b.IsTrigger && b.Type.Equals(WarmupTriggerName, StringComparison.OrdinalIgnoreCase));
            });
        }

        /// <summary>
        /// Try to invoke a warmup function if available
        /// </summary>
        /// <returns>
        /// A task that represents the asynchronous operation.
        /// The task results true if a warmup function was invoked, false otherwise.
        /// </returns>
        public static async Task<bool> TryInvokeWarmupAsync(this IScriptJobHost scriptJobHost)
        {
            var warmupFunction = scriptJobHost.GetWarmupFunctionOrNull();
            if (warmupFunction != null)
            {
                ParameterDescriptor inputParameter = warmupFunction.Parameters.First(p => p.IsTrigger);

                var arguments = new Dictionary<string, object>()
                {
                    { inputParameter.Name, new WarmupContext() }
                };

                await scriptJobHost.CallAsync(warmupFunction.Name, arguments);
                return true;
            }

            return false;
        }
    }
}
