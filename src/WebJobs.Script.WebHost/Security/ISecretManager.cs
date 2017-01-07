﻿// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System.Collections.Generic;
using System.Threading.Tasks;
using Microsoft.Azure.WebJobs.Host;

namespace Microsoft.Azure.WebJobs.Script.WebHost
{
    public interface ISecretManager
    {
        /// <summary>
        /// Deletes a function secret.
        /// </summary>
        /// <param name="secretName">The name of the secret to be deleted.</param>
        /// <param name="functionName">The function name, in case of a function level secret; <see cref="null"/> if this is a host level function secret.</param>
        /// <returns>True if the secret was successfully deleted; otherwise, false.</returns>
        Task<bool> DeleteSecretAsync(string secretName, string functionName = null);

        /// <summary>
        /// Retrieves function secrets.
        /// </summary>
        /// <param name="functionName">The name of the function.</param>
        /// <param name="merged">True if the results should include host level secrets (where function secrets would take priority); otherwise, false.</param>
        /// <returns>A <see cref="IDictionary{string, string}"/> containing the named function secrets.</returns>
        Task<IDictionary<string, string>> GetFunctionSecretsAsync(string functionName, bool merged = false);

        /// <summary>
        /// Retrieves the host secrets.
        /// </summary>
        /// <returns>A <see cref="HostSecretsInfo"/> instance containing the host secrets.</returns>
        Task<HostSecretsInfo> GetHostSecretsAsync();

        /// <summary>
        /// Adds a function secret to the specified function (or the host if a function is not specified) if the secret does
        /// not already exist, or updates the secret if it does.
        /// </summary>
        /// <param name="secretName">The name of the secret to be created or updated.</param>
        /// <param name="secret">The secret value.</param>
        /// <param name="functionName">The optional function name. If not provided, the function secret will be created at the host level.</param>
        /// <returns>A <see cref="Task"/> that completes when the operation is finished.</returns>
        Task<KeyOperationResult> AddOrUpdateFunctionSecretAsync(string secretName, string secret, string functionName = null);

        /// <summary>
        /// Updates the host master key.
        /// </summary>
        /// <param name="value">Optional value. If <see cref="null"/>, the value will be auto-generated.</param>
        /// <returns>A <see cref="KeyOperationResult"/> instance representing the result of this operation.</returns>
        Task<KeyOperationResult> SetMasterKeyAsync(string value = null);

        /// <summary>
        /// Iterate through all function secrets and remove any that don't correspond
        /// to a function.
        /// </summary>
        /// <param name="rootScriptPath">The root function directory.</param>
        /// <param name="traceWriter">The TraceWriter to log to.</param>
        Task PurgeOldSecretsAsync(string rootScriptPath, TraceWriter traceWriter);
    }
}