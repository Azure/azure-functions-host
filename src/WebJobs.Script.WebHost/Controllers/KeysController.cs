// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Web.Http;
using Microsoft.Azure.WebJobs.Script.WebHost.Filters;
using Microsoft.Azure.WebJobs.Script.WebHost.Models;

namespace Microsoft.Azure.WebJobs.Script.WebHost.Controllers
{
    [AuthorizationLevel(AuthorizationLevel.Admin)]
    public class KeysController : ApiController
    {
        private const string MasterKeyName = "_master";

        private readonly WebScriptHostManager _scriptHostManager;
        private readonly ISecretManager _secretManager;

        public KeysController(WebScriptHostManager scriptHostManager, ISecretManager secretManager)
        {
            _scriptHostManager = scriptHostManager;
            _secretManager = secretManager;
        }

        [HttpGet]
        [Route("admin/functions/{name}/keys")]
        public IHttpActionResult Get(string name)
        {
            if (!_scriptHostManager.Instance.Functions.Any(f => string.Equals(f.Name, name, StringComparison.OrdinalIgnoreCase)))
            {
                return NotFound();
            }

            var functionKeys = _secretManager.GetFunctionSecrets(name);
            return GetKeysResult(functionKeys);
        }

        [HttpGet]
        [Route("admin/host/keys")]
        public IHttpActionResult Get()
        {
            var hostSecrets = _secretManager.GetHostSecrets();
            return GetKeysResult(hostSecrets.FunctionKeys);
        }

        [HttpPost]
        [Route("admin/functions/{name}/keys/{keyName}")]
        public IHttpActionResult Post(string name, string keyName) => AddOrUpdateFunctionSecret(keyName, null, name);

        [HttpPost]
        [Route("admin/host/keys/{keyName}")]
        public IHttpActionResult Post(string keyName) => AddOrUpdateFunctionSecret(keyName, null);

        [HttpPut]
        [Route("admin/functions/{name}/keys/{keyName}")]
        public IHttpActionResult Put(string name, string keyName, Key key) => PutKey(keyName, key, name);

        [HttpPut]
        [Route("admin/host/keys/{keyName}")]
        public IHttpActionResult Put(string keyName, Key key) => PutKey(keyName, key);

        [HttpDelete]
        [Route("admin/functions/{name}/keys/{keyName}")]
        public IHttpActionResult Delete(string name, string keyName) => DeleteFunctionSecret(keyName, name);

        [HttpDelete]
        [Route("admin/host/keys/{keyName}")]
        public IHttpActionResult Delete(string keyName) => DeleteFunctionSecret(keyName);

        private IHttpActionResult GetKeysResult(IDictionary<string, string> keys)
        {
            var keysContent = new
            {
                keys = keys.Select(k => new { name = k.Key, value = k.Value })
            };

            var keyResponse = ApiModelUtility.CreateApiModel(keysContent, Request);

            return Ok(keyResponse);
        }

        private IHttpActionResult PutKey(string keyName, Key key, string functionName = null)
        {
            if (key?.Value == null)
            {
                return BadRequest("Invalid key value");
            }

            return AddOrUpdateFunctionSecret(keyName, key.Value, functionName);
        }

        private IHttpActionResult AddOrUpdateFunctionSecret(string keyName, string value, string functionName = null)
        {
            if (functionName != null &&
                !_scriptHostManager.Instance.Functions.Any(f => string.Equals(f.Name, functionName, StringComparison.OrdinalIgnoreCase)))
            {
                return NotFound();
            }

            KeyOperationResult operationResult;
            if (functionName == null && string.Equals(keyName, MasterKeyName, StringComparison.OrdinalIgnoreCase))
            {
                operationResult = _secretManager.SetMasterKey(value);
            }
            else
            {
                operationResult = _secretManager.AddOrUpdateFunctionSecret(keyName, value, functionName);
            }

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

        private IHttpActionResult DeleteFunctionSecret(string keyName, string functionName = null)
        {
            if (keyName == null || keyName.StartsWith("_"))
            {
                // System keys cannot be deleted.
                return BadRequest("Invalid key name.");
            }

            if ((functionName != null && !_scriptHostManager.Instance.Functions.Any(f => string.Equals(f.Name, functionName, StringComparison.OrdinalIgnoreCase))) ||
                !_secretManager.DeleteSecret(keyName, functionName))
            {
                // Either the function or the key were not found
                return NotFound();
            }

            return StatusCode(HttpStatusCode.NoContent);
        }
    }
}
