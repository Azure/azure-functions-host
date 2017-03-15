﻿// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System;
using System.Net;
using System.Threading.Tasks;
using System.Web.Http;
using Microsoft.Azure.WebJobs.Host;
using Microsoft.Azure.WebJobs.Script.WebHost.Filters;
using Microsoft.Azure.WebJobs.Script.WebHost.Properties;
using Newtonsoft.Json.Linq;

namespace Microsoft.Azure.WebJobs.Script.WebHost.Controllers
{
    [SystemAuthorizationLevel(ScriptConstants.SwaggerDocumentationKey)]
    public class SwaggerController : ApiController
    {
        private readonly ISwaggerDocumentManager _swaggerDocumentManager;
        private readonly WebScriptHostManager _scriptHostManager;
        private readonly TraceWriter _traceWriter;

        public SwaggerController(ISwaggerDocumentManager swaggerDocumentManager, WebScriptHostManager scriptHostManager, TraceWriter traceWriter)
        {
            _swaggerDocumentManager = swaggerDocumentManager;
            _scriptHostManager = scriptHostManager;
            _traceWriter = traceWriter.WithSource($"{ScriptConstants.TraceSourceSwagger}.Api");
        }

        [HttpGet]
        [Route("admin/host/swagger/default")]
        public IHttpActionResult GetGeneratedSwaggerDocument()
        {
            try
            {
                _traceWriter.Verbose(Resources.SwaggerGenerateDocument);
                var swaggerDocument = _swaggerDocumentManager.GenerateSwaggerDocument(_scriptHostManager.HttpFunctions);
                return Ok(swaggerDocument);
            }
            catch (Exception ex)
            {
                _traceWriter.Error(Resources.SwaggerGenerateError, ex);
                return InternalServerError();
            }
        }

        [HttpGet]
        [Route("admin/host/swagger")]
        public async Task<IHttpActionResult> GetSwaggerDocumentAsync()
        {
            IHttpActionResult result = NotFound();
            try
            {
                if (_scriptHostManager.Instance.ScriptConfig.SwaggerEnabled)
                {
                    var swaggerDocument = await _swaggerDocumentManager.GetSwaggerDocumentAsync();
                    if (swaggerDocument != null)
                    {
                        result = Ok(swaggerDocument);
                    }
                }
            }
            catch (Exception ex)
            {
                _traceWriter.Error(Resources.SwaggerFileReadError, ex);
                result = InternalServerError();
            }
            return result;
        }

        [HttpPost]
        [Route("admin/host/swagger")]
        public async Task<IHttpActionResult> AddOrUpdateSwaggerDocumentAsync([FromBody] JObject swaggerDocumentJson)
        {
            try
            {
                var updatedSwaggerDocumentJson = await _swaggerDocumentManager.AddOrUpdateSwaggerDocumentAsync(swaggerDocumentJson);
                return Ok(updatedSwaggerDocumentJson);
            }
            catch (Exception ex)
            {
                _traceWriter.Error(Resources.SwaggerFileUpdateError, ex);
                return InternalServerError();
            }
        }

        [HttpDelete]
        [Route("admin/host/swagger")]
        public async Task<IHttpActionResult> DeleteSwaggerDocumentAsync()
        {
            try
            {
                var deleted = await _swaggerDocumentManager.DeleteSwaggerDocumentAsync();
                if (deleted)
                {
                    return StatusCode(HttpStatusCode.NoContent);
                }
                return NotFound();
            }
            catch (Exception ex)
            {
                _traceWriter.Error(String.Format(Resources.SwaggerFileDeleteError), ex);
                return InternalServerError();
            }
        }
    }
}