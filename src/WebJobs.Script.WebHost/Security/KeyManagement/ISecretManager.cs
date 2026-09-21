// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System.Collections.Generic;
using System.Threading.Tasks;
using Microsoft.Azure.WebJobs.Extensions.Http;
using Microsoft.Extensions.Logging;

namespace Microsoft.Azure.WebJobs.Script.WebHost
{
    public interface ISecretManager
    {
        /// <summary>
        /// Deterine the <see cref="AuthorizationLevel"/> for the specified key.
        /// </summary>
        /// <param name="key">The key to check.</param>
        /// <param name="functionName">Optional function name, if we're authorizing a specific function.</param>
        /// <returns>A key name, auth level pair.</returns>
        Task<(string KeyName, AuthorizationLevel Level)> GetAuthorizationLevelOrNullAsync(string key, string functionName = null);

        /// <summary>
        /// Deletes a function secret.
        /// </summary>
        /// <param name="secretName">The name of the secret to be deleted.</param>
        /// <param name="keyScope">The target scope for the key. In case of a function level secrets, this will be the name of the function,
        /// for host level secrets, this will identify the host secret type.</param>
        /// <param name="secretsType">The target secrets type.</param>
        /// <returns>True if the secret was successfully deleted; otherwise, false.</returns>
        Task<bool> DeleteSecretAsync(string secretName, string keyScope, ScriptSecretsType secretsType);

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
        /// <param name="keyScope">The target scope for the key. For function level secrets, this will be the name of the function,
        /// for host level secrets, this will identify the host secret type.</param>
        /// <param name="secretsType">The target secrets type.</param>
        /// <returns>A <see cref="Task"/> that completes when the operation is finished.</returns>
        Task<KeyOperationResult> AddOrUpdateFunctionSecretAsync(string secretName, string secret, string keyScope, ScriptSecretsType secretsType);

        /// <summary>
        /// Gets the value of the named host system key, creating and persisting a new key if one does not already exist.
        /// </summary>
        /// <remarks>
        /// Unlike a direct <see cref="GetHostSecretsAsync"/> lookup, this method guards against a stale host secrets
        /// cache. The cache can be seeded from a one-time startup context snapshot that predates a key, so a simple
        /// cache miss is not a reliable signal that the key is absent. Before generating a new key, this method
        /// re-reads authoritative secrets from storage, ensuring an existing key is never overwritten (which would
        /// invalidate previously issued extension webhook URLs).
        /// </remarks>
        /// <param name="keyName">The name of the system key to get or create.</param>
        /// <returns>The value of the existing or newly created system key.</returns>
        Task<string> GetOrCreateSystemKeyAsync(string keyName);

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
        /// <param name="logger">The ILogger to log to.</param>
        Task PurgeOldSecretsAsync(string rootScriptPath, ILogger logger);

        /// <summary>
        /// If secrets have been loaded and cached, clear all cached secrets so they'll
        /// be reloaded next time they're requested.
        /// </summary>
        void ClearCache();
    }
}