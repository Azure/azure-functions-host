// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Abstractions;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Azure.WebJobs.Script.WebHost.Management;
using Microsoft.Azure.WebJobs.Script.WebHost.Models;
using Microsoft.Azure.WebJobs.Script.WebHost.Properties;
using Microsoft.Azure.WebJobs.Script.WebHost.Security.Authorization.Policies;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Microsoft.Azure.WebJobs.Script.WebHost.Controllers
{
    [Authorize(Policy = PolicyNames.AdminAuthLevel)]
    public class KeysController : Controller
    {
        private const string MasterKeyName = "_master";

        private static readonly Lazy<Dictionary<string, string>> EmptyKeys = new Lazy<Dictionary<string, string>>(() => new Dictionary<string, string>());
        private readonly ISecretManagerProvider _secretManagerProvider;
        private readonly ILogger _logger;
        private readonly IOptions<ScriptApplicationHostOptions> _applicationOptions;
        private readonly IFileSystem _fileSystem;
        private readonly IFunctionsSyncManager _functionsSyncManager;
        private readonly IScriptHostManager _hostManager;

        public KeysController(IOptions<ScriptApplicationHostOptions> applicationOptions, ISecretManagerProvider secretManagerProvider, ILoggerFactory loggerFactory, IFileSystem fileSystem, IFunctionsSyncManager functionsSyncManager, IScriptHostManager hostManager)
        {
            _applicationOptions = applicationOptions;
            _secretManagerProvider = secretManagerProvider;
            _logger = loggerFactory.CreateLogger(ScriptConstants.LogCategoryKeysController);
            _fileSystem = fileSystem;
            _functionsSyncManager = functionsSyncManager;
            _hostManager = hostManager;
        }

        [HttpGet]
        [Route("admin/functions/{name}/keys")]
        public async Task<IActionResult> Get(string name)
        {
            IDictionary<string, string> functionKeys = await GetFunctionKeys(name);

            if (functionKeys == null)
            {
                return NotFound();
            }

            return GetKeysResult(functionKeys);
        }

        [HttpGet]
        [Route("admin/host/{keys:regex(^(keys|functionkeys|systemkeys)$)}")]
        public async Task<IActionResult> Get()
        {
            string hostKeyScope = GetHostKeyScopeForRequest();

            if (string.Equals(hostKeyScope, HostKeyScopes.SystemKeys, StringComparison.OrdinalIgnoreCase) &&
                _hostManager.State != ScriptHostState.Offline)
            {
                // Extensions that are webhook providers create their default system keys
                // as part of host initialization (when those keys aren't already present).
                // So we must delay key retrieval until host initialization is complete.
                await _hostManager.DelayUntilHostReady();
            }

            Dictionary<string, string> keys = await GetHostSecretsByScope(hostKeyScope);
            return GetKeysResult(keys);
        }

        [HttpGet]
        [Route("admin/functions/{functionName}/keys/{name}")]
        public async Task<IActionResult> Get(string functionName, string name)
        {
            IDictionary<string, string> functionKeys = await GetFunctionKeys(functionName);
            if (functionKeys != null && functionKeys.TryGetValue(name, out string keyValue))
            {
                var keyResponse = ApiModelUtility.CreateApiModel(new { name = name, value = keyValue }, Request);
                return Ok(keyResponse);
            }

            return NotFound();
        }

        [HttpGet]
        [Route("admin/host/{keys:regex(^(keys|functionkeys|systemkeys)$)}/{name}")]
        public async Task<IActionResult> GetHostKey(string name)
        {
            string hostKeyScope = GetHostKeyScopeForRequest();

            Dictionary<string, string> keys = await GetHostSecretsByScope(hostKeyScope, true);

            string keyValue = null;
            if (keys?.TryGetValue(name, out keyValue) ?? false)
            {
                var keyResponse = ApiModelUtility.CreateApiModel(new { name = name, value = keyValue }, Request);
                return Ok(keyResponse);
            }

            return NotFound();
        }

        private async Task<IDictionary<string, string>> GetFunctionKeys(string functionName)
        {
            if (!IsFunction(functionName))
            {
                return null;
            }

            return await _secretManagerProvider.Current.GetFunctionSecretsAsync(functionName);
        }

        [HttpPost]
        [Route("admin/functions/{name}/keys/{keyName}")]
        public Task<IActionResult> Post(string name, string keyName) => AddOrUpdateSecretAsync(keyName, null, name, ScriptSecretsType.Function);

        [HttpPost]
        [Route("admin/host/{keys:regex(^(keys|functionkeys|systemkeys)$)}/{keyName}")]
        public Task<IActionResult> Post(string keyName) => AddOrUpdateSecretAsync(keyName, null, GetHostKeyScopeForRequest(), ScriptSecretsType.Host);

        [HttpPut]
        [Route("admin/functions/{name}/keys/{keyName}")]
        public Task<IActionResult> Put(string name, string keyName, [FromBody] Key key) => PutKeyAsync(keyName, key, name, ScriptSecretsType.Function);

        [HttpPut]
        [Route("admin/host/{keys:regex(^(keys|functionkeys|systemkeys)$)}/{keyName}")]
        public Task<IActionResult> Put(string keyName, [FromBody] Key key) => PutKeyAsync(keyName, key, GetHostKeyScopeForRequest(), ScriptSecretsType.Host);

        [HttpDelete]
        [Route("admin/functions/{name}/keys/{keyName}")]
        public Task<IActionResult> Delete(string name, string keyName) => DeleteFunctionSecretAsync(keyName, name, ScriptSecretsType.Function);

        [HttpDelete]
        [Route("admin/host/{keys:regex(^(keys|functionkeys|systemkeys)$)}/{keyName}")]
        public Task<IActionResult> Delete(string keyName) => DeleteFunctionSecretAsync(keyName, GetHostKeyScopeForRequest(), ScriptSecretsType.Host);

        private string GetHostKeyScopeForRequest()
        {
            string keyScope = ControllerContext.RouteData.Values.GetValueOrDefault("keys")?.ToString();

            if (string.Equals(keyScope, "keys", StringComparison.OrdinalIgnoreCase))
            {
                keyScope = HostKeyScopes.FunctionKeys;
            }

            return keyScope;
        }

        private IActionResult GetKeysResult(IDictionary<string, string> keys)
        {
            keys = keys ?? EmptyKeys.Value;
            var keysContent = new
            {
                keys = keys.Select(k => new { name = k.Key, value = k.Value })
            };

            var keyResponse = ApiModelUtility.CreateApiModel(keysContent, Request);

            return Ok(keyResponse);
        }

        private async Task<IActionResult> PutKeyAsync(string keyName, Key key, string keyScope, ScriptSecretsType secretsType)
        {
            if (key?.Value == null)
            {
                return BadRequest("Invalid key value");
            }

            return await AddOrUpdateSecretAsync(keyName, key.Value, keyScope, secretsType);
        }

        private async Task<IActionResult> AddOrUpdateSecretAsync(string keyName, string value, string keyScope, ScriptSecretsType secretsType)
        {
            if (secretsType == ScriptSecretsType.Function && keyScope != null && !IsFunction(keyScope))
            {
                return NotFound();
            }

            KeyOperationResult operationResult;
            if (secretsType == ScriptSecretsType.Host && string.Equals(keyName, MasterKeyName, StringComparison.OrdinalIgnoreCase))
            {
                operationResult = await _secretManagerProvider.Current.SetMasterKeyAsync(value);
            }
            else
            {
                operationResult = await _secretManagerProvider.Current.AddOrUpdateFunctionSecretAsync(keyName, value, keyScope, secretsType);
            }

            _logger.LogDebug(string.Format(Resources.TraceKeysApiSecretChange, keyName, keyScope ?? "host", operationResult.Result));

            switch (operationResult.Result)
            {
                case OperationResult.Created:
                    {
                        var keyResponse = ApiModelUtility.CreateApiModel(new { name = keyName, value = operationResult.Secret }, Request);
                        await _functionsSyncManager.TrySyncTriggersAsync();
                        return Created(ApiModelUtility.GetBaseUri(Request), keyResponse);
                    }
                case OperationResult.Updated:
                    {
                        var keyResponse = ApiModelUtility.CreateApiModel(new { name = keyName, value = operationResult.Secret }, Request);
                        await _functionsSyncManager.TrySyncTriggersAsync();
                        return Ok(keyResponse);
                    }
                case OperationResult.NotFound:
                    return NotFound();
                case OperationResult.Conflict:
                    return StatusCode(StatusCodes.Status409Conflict);
                case OperationResult.Forbidden:
                    return StatusCode(StatusCodes.Status403Forbidden);
                default:
                    return StatusCode(StatusCodes.Status500InternalServerError);
            }
        }

        private async Task<Dictionary<string, string>> GetHostSecretsByScope(string secretsScope, bool includeMasterInSystemKeys = false)
        {
            var hostSecrets = await _secretManagerProvider.Current.GetHostSecretsAsync();

            if (string.Equals(secretsScope, HostKeyScopes.FunctionKeys, StringComparison.OrdinalIgnoreCase))
            {
                return hostSecrets.FunctionKeys;
            }
            else if (string.Equals(secretsScope, HostKeyScopes.SystemKeys, StringComparison.OrdinalIgnoreCase))
            {
                Dictionary<string, string> keys = hostSecrets.SystemKeys ?? new Dictionary<string, string>();

                if (includeMasterInSystemKeys)
                {
                    keys = new Dictionary<string, string>(keys)
                    {
                        { MasterKeyName, hostSecrets.MasterKey }
                    };
                }

                return keys;
            }

            return null;
        }

        private async Task<IActionResult> DeleteFunctionSecretAsync(string keyName, string keyScope, ScriptSecretsType secretsType)
        {
            if (keyName == null || keyName.StartsWith("_"))
            {
                // System keys cannot be deleted.
                return BadRequest("Invalid key name.");
            }

            if ((secretsType == ScriptSecretsType.Function && keyScope != null && !IsFunction(keyScope)) ||
                !await _secretManagerProvider.Current.DeleteSecretAsync(keyName, keyScope, secretsType))
            {
                // Either the function or the key were not found
                return NotFound();
            }

            await _functionsSyncManager.TrySyncTriggersAsync();

            _logger.LogDebug(string.Format(Resources.TraceKeysApiSecretChange, keyName, keyScope ?? "host", "Deleted"));

            return StatusCode(StatusCodes.Status204NoContent);
        }

        private bool IsFunction(string functionName)
        {
            string json = null;
            string functionPath = Path.Combine(_applicationOptions.Value.ScriptPath, functionName);
            return Utility.TryReadFunctionConfig(functionPath, out json, _fileSystem);
        }
    }
}