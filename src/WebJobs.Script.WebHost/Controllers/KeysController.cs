﻿// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Abstractions;
using System.Linq;
using System.Net;
using System.Threading.Tasks;
using System.Web.Http;
using Microsoft.Azure.WebJobs.Extensions.Http;
using Microsoft.Azure.WebJobs.Host;
using Microsoft.Azure.WebJobs.Script.WebHost.Filters;
using Microsoft.Azure.WebJobs.Script.WebHost.Management;
using Microsoft.Azure.WebJobs.Script.WebHost.Models;
using Microsoft.Azure.WebJobs.Script.WebHost.Properties;
using Microsoft.Extensions.Logging;

namespace Microsoft.Azure.WebJobs.Script.WebHost.Controllers
{
    [JwtAuthentication]
    [AuthorizationLevel(AuthorizationLevel.Admin)]
    [ResourceContainsSecrets]
    public class KeysController : ApiController
    {
        private const string MasterKeyName = "_master";

        private static readonly Lazy<Dictionary<string, string>> EmptyKeys = new Lazy<Dictionary<string, string>>(() => new Dictionary<string, string>());
        private readonly ISecretManager _secretManager;
        private readonly TraceWriter _traceWriter;
        private readonly ILogger _logger;
        private readonly WebHostSettings _settings;
        private readonly IFileSystem _fileSystem;
        private readonly IFunctionsSyncManager _functionsSyncManager;

        public KeysController(WebHostSettings settings, ISecretManager secretManager, TraceWriter traceWriter, ILoggerFactory loggerFactory, IFileSystem fileSystem, IFunctionsSyncManager functionsSyncManager)
        {
            _settings = settings;
            _secretManager = secretManager;
            _traceWriter = traceWriter.WithDefaults($"{ScriptConstants.TraceSourceSecretManagement}.Api");
            _logger = loggerFactory?.CreateLogger(ScriptConstants.LogCategoryKeysController);
            _fileSystem = fileSystem;
            _functionsSyncManager = functionsSyncManager;
        }

        [HttpGet]
        [Route("admin/functions/{name}/keys")]
        public async Task<IHttpActionResult> Get(string name)
        {
            var keys = await GetFunctionKeys(name);
            return GetKeysResult(keys);
        }

        [HttpGet]
        [Route("admin/host/{keys:regex(^(keys|functionkeys|systemkeys)$)}")]
        public async Task<IHttpActionResult> Get()
        {
            string hostKeyScope = GetHostKeyScopeForRequest();

            var keys = await GetHostSecretsByScope(hostKeyScope);
            return GetKeysResult(keys);
        }

        [HttpGet]
        [Route("admin/functions/{functionName}/keys/{name}")]
        public async Task<IHttpActionResult> Get(string functionName, string name)
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
        public async Task<IHttpActionResult> GetHostKey(string name)
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

            return await _secretManager.GetFunctionSecretsAsync(functionName);
        }

        [HttpPost]
        [Route("admin/functions/{name}/keys/{keyName}")]
        public Task<IHttpActionResult> Post(string name, string keyName) => AddOrUpdateSecretAsync(keyName, null, name, ScriptSecretsType.Function);

        [HttpPost]
        [Route("admin/host/{keys:regex(^(keys|functionkeys|systemkeys)$)}/{keyName}")]
        public Task<IHttpActionResult> Post(string keyName) => AddOrUpdateSecretAsync(keyName, null, GetHostKeyScopeForRequest(), ScriptSecretsType.Host);

        [HttpPut]
        [Route("admin/functions/{name}/keys/{keyName}")]
        public Task<IHttpActionResult> Put(string name, string keyName, Key key) => PutKeyAsync(keyName, key, name, ScriptSecretsType.Function);

        [HttpPut]
        [Route("admin/host/{keys:regex(^(keys|functionkeys|systemkeys)$)}/{keyName}")]
        public Task<IHttpActionResult> Put(string keyName, Key key) => PutKeyAsync(keyName, key, GetHostKeyScopeForRequest(), ScriptSecretsType.Host);

        [HttpDelete]
        [Route("admin/functions/{name}/keys/{keyName}")]
        public Task<IHttpActionResult> Delete(string name, string keyName) => DeleteFunctionSecretAsync(keyName, name, ScriptSecretsType.Function);

        [HttpDelete]
        [Route("admin/host/{keys:regex(^(keys|functionkeys|systemkeys)$)}/{keyName}")]
        public Task<IHttpActionResult> Delete(string keyName) => DeleteFunctionSecretAsync(keyName, GetHostKeyScopeForRequest(), ScriptSecretsType.Host);

        private string GetHostKeyScopeForRequest()
        {
            string keyScope = ControllerContext.RouteData.Values.GetValueOrDefault<string>("keys");

            if (string.Equals(keyScope, "keys", StringComparison.OrdinalIgnoreCase))
            {
                keyScope = HostKeyScopes.FunctionKeys;
            }

            return keyScope;
        }

        private IHttpActionResult GetKeysResult(IDictionary<string, string> keys)
        {
            if (keys == null)
            {
                return NotFound();
            }

            keys = keys ?? EmptyKeys.Value;
            var keysContent = new
            {
                keys = keys.Select(k => new { name = k.Key, value = k.Value })
            };

            var keyResponse = ApiModelUtility.CreateApiModel(keysContent, Request);

            return Ok(keyResponse);
        }

        private async Task<IHttpActionResult> PutKeyAsync(string keyName, Key key, string keyScope, ScriptSecretsType secretsType)
        {
            if (key?.Value == null)
            {
                return BadRequest("Invalid key value");
            }

            return await AddOrUpdateSecretAsync(keyName, key.Value, keyScope, secretsType);
        }

        private async Task<IHttpActionResult> AddOrUpdateSecretAsync(string keyName, string value, string keyScope, ScriptSecretsType secretsType)
        {
            if (secretsType == ScriptSecretsType.Function && keyScope != null && !IsFunction(keyScope))
            {
                return NotFound();
            }

            KeyOperationResult operationResult;
            if (secretsType == ScriptSecretsType.Host && string.Equals(keyName, MasterKeyName, StringComparison.OrdinalIgnoreCase))
            {
                operationResult = await _secretManager.SetMasterKeyAsync(value);
            }
            else
            {
                operationResult = await _secretManager.AddOrUpdateFunctionSecretAsync(keyName, value, keyScope, secretsType);
            }

            string message = string.Format(Resources.TraceKeysApiSecretChange, keyName, keyScope ?? "host", operationResult.Result);
            _traceWriter.Info(message);
            _logger?.LogInformation(message);

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
                    return Conflict();
                default:
                    return InternalServerError();
            }
        }

        private async Task<Dictionary<string, string>> GetHostSecretsByScope(string secretsScope, bool includeMasterInSystemKeys = false)
        {
            var hostSecrets = await _secretManager.GetHostSecretsAsync();

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

        private async Task<IHttpActionResult> DeleteFunctionSecretAsync(string keyName, string keyScope, ScriptSecretsType secretsType)
        {
            if (keyName == null)
            {
                {
                    // System keys cannot be deleted.	
                    return BadRequest("Invalid key name."); return BadRequest("Invalid key name.");
                }
            }


            if (IsBuiltInSystemKeyName(keyName))
            {
                // System keys cannot be deleted.
                return BadRequest("Cannot delete System Key.");
            }


            if ((secretsType == ScriptSecretsType.Function && keyScope != null && !IsFunction(keyScope)) || 
                !await _secretManager.DeleteSecretAsync(keyName, keyScope, secretsType))
            {
                // the key was not found
                return NotFound();
            }

            await _functionsSyncManager.TrySyncTriggersAsync();

            string message = string.Format(Resources.TraceKeysApiSecretChange, keyName, keyScope ?? "host", "Deleted");
            _traceWriter.Info(message);
            _logger?.LogInformation(message);

            return StatusCode(HttpStatusCode.NoContent);
        }

        internal bool IsBuiltInSystemKeyName(string keyName)
        {
            if (keyName.Equals(MasterKeyName, StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }
            return false;
        }

        private bool IsFunction(string functionName)
        {
            string json = null;
            string functionPath = Path.Combine(_settings.ScriptPath, functionName);
            return ScriptHost.TryReadFunctionConfig(functionPath, out json, _fileSystem);
        }
    }
}
