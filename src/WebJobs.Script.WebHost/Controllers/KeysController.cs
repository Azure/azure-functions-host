// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Net;
using System.Threading.Tasks;
using System.Web.Http;
using Microsoft.Azure.WebJobs.Host;
using Microsoft.Azure.WebJobs.Script.WebHost.Filters;
using Microsoft.Azure.WebJobs.Script.WebHost.Models;
using Microsoft.Azure.WebJobs.Script.WebHost.Properties;

namespace Microsoft.Azure.WebJobs.Script.WebHost.Controllers
{
    [AuthorizationLevel(AuthorizationLevel.Admin)]
    public class KeysController : ApiController
    {
        private const string MasterKeyName = "_master";

        private readonly WebScriptHostManager _scriptHostManager;
        private readonly ISecretManager _secretManager;
        private readonly TraceWriter _traceWriter;

        public KeysController(WebScriptHostManager scriptHostManager, ISecretManager secretManager, TraceWriter traceWriter)
        {
            _scriptHostManager = scriptHostManager;
            _secretManager = secretManager;
            _traceWriter = traceWriter.WithSource($"{ScriptConstants.TraceSourceSecretManagement}.Api");
        }

        [HttpGet]
        [Route("admin/functions/{name}/keys")]
        public async Task<IHttpActionResult> Get(string name)
        {
            if (!_scriptHostManager.Instance.Functions.Any(f => string.Equals(f.Name, name, StringComparison.OrdinalIgnoreCase)))
            {
                return NotFound();
            }

            var functionKeys = await _secretManager.GetFunctionSecretsAsync(name);
            return GetKeysResult(functionKeys);
        }

        [HttpGet]
        [Route("admin/host/keys")]
        public async Task<IHttpActionResult> Get()
        {
            var hostSecrets = await _secretManager.GetHostSecretsAsync();
            return GetKeysResult(hostSecrets.FunctionKeys);
        }

        [HttpPost]
        [Route("admin/functions/{name}/keys/{keyName}")]
        public Task<IHttpActionResult> Post(string name, string keyName) => AddOrUpdateFunctionSecretAsync(keyName, null, name);

        [HttpPost]
        [Route("admin/host/keys/{keyName}")]
        public Task<IHttpActionResult> Post(string keyName) => AddOrUpdateFunctionSecretAsync(keyName, null);

        [HttpPut]
        [Route("admin/functions/{name}/keys/{keyName}")]
        public Task<IHttpActionResult> Put(string name, string keyName, Key key) => PutKeyAsync(keyName, key, name);

        [HttpPut]
        [Route("admin/host/keys/{keyName}")]
        public Task<IHttpActionResult> Put(string keyName, Key key) => PutKeyAsync(keyName, key);

        [HttpDelete]
        [Route("admin/functions/{name}/keys/{keyName}")]
        public Task<IHttpActionResult> Delete(string name, string keyName) => DeleteFunctionSecretAsync(keyName, name);

        [HttpDelete]
        [Route("admin/host/keys/{keyName}")]
        public Task<IHttpActionResult> Delete(string keyName) => DeleteFunctionSecretAsync(keyName);

        private IHttpActionResult GetKeysResult(IDictionary<string, string> keys)
        {
            var keysContent = new
            {
                keys = keys.Select(k => new { name = k.Key, value = k.Value })
            };

            var keyResponse = ApiModelUtility.CreateApiModel(keysContent, Request);

            return Ok(keyResponse);
        }

        private async Task<IHttpActionResult> PutKeyAsync(string keyName, Key key, string functionName = null)
        {
            if (key?.Value == null)
            {
                return BadRequest("Invalid key value");
            }

            return await AddOrUpdateFunctionSecretAsync(keyName, key.Value, functionName);
        }

        private async Task<IHttpActionResult> AddOrUpdateFunctionSecretAsync(string keyName, string value, string functionName = null)
        {
            if (functionName != null &&
                !_scriptHostManager.Instance.Functions.Any(f => string.Equals(f.Name, functionName, StringComparison.OrdinalIgnoreCase)))
            {
                return NotFound();
            }

            KeyOperationResult operationResult;
            if (functionName == null && string.Equals(keyName, MasterKeyName, StringComparison.OrdinalIgnoreCase))
            {
                operationResult = await _secretManager.SetMasterKeyAsync(value);
            }
            else
            {
                operationResult = await _secretManager.AddOrUpdateFunctionSecretAsync(keyName, value, functionName);
            }

            _traceWriter.VerboseFormat(Resources.TraceKeysApiSecretChange, keyName, functionName ?? "host", operationResult.Result);

            switch (operationResult.Result)
            {
                case OperationResult.Created:
                    {
                        var keyResponse = ApiModelUtility.CreateApiModel(new { name = keyName, value = operationResult.Secret }, Request);
                        return Created(ApiModelUtility.GetBaseUri(Request), keyResponse);
                    }
                case OperationResult.Updated:
                    {
                        var keyResponse = ApiModelUtility.CreateApiModel(new { name = keyName, value = operationResult.Secret }, Request);
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

        private async Task<IHttpActionResult> DeleteFunctionSecretAsync(string keyName, string functionName = null)
        {
            if (keyName == null || keyName.StartsWith("_"))
            {
                // System keys cannot be deleted.
                return BadRequest("Invalid key name.");
            }

            if ((functionName != null && !_scriptHostManager.Instance.Functions.Any(f => string.Equals(f.Name, functionName, StringComparison.OrdinalIgnoreCase))) ||
                !await _secretManager.DeleteSecretAsync(keyName, functionName))
            {
                // Either the function or the key were not found
                return NotFound();
            }

            _traceWriter.VerboseFormat(Resources.TraceKeysApiSecretChange, keyName, functionName ?? "host", "Deleted");

            return StatusCode(HttpStatusCode.NoContent);
        }
    }
}
