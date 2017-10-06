// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.Extensions;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Azure.WebJobs.Script.Description;
using Microsoft.Azure.WebJobs.Script.Management.Models;
using Microsoft.Azure.WebJobs.Script.WebHost.Filters;
using Microsoft.Azure.WebJobs.Script.WebHost.Management;
using Microsoft.Azure.WebJobs.Script.WebHost.Models;
using Microsoft.Azure.WebJobs.Script.WebHost.Security.Authorization.Policies;
using Microsoft.Extensions.Logging;

namespace Microsoft.Azure.WebJobs.Script.WebHost.Controllers
{
    /// <summary>
    /// Controller responsible for administrative and management operations on functions
    /// example retriving a list of functions, invoking a function, creating a function, etc
    /// </summary>
    public class FunctionsController : Controller
    {
        private readonly IWebFunctionsManager _functionsManager;
        private readonly ScriptHostManager _scriptHostManager;
        private readonly ILogger _logger;
        private static readonly Regex FunctionNameValidationRegex = new Regex(@"^[a-z][a-z0-9_\-]{0,127}$(?<!^host$)", RegexOptions.Compiled | RegexOptions.IgnoreCase);

        public FunctionsController(IWebFunctionsManager functionsManager, WebScriptHostManager scriptHostManager, ILoggerFactory loggerFactory)
        {
            _functionsManager = functionsManager;
            _scriptHostManager = scriptHostManager;
            _logger = loggerFactory?.CreateLogger(ScriptConstants.LogCategoryFunctionsController);
        }

        [HttpGet]
        [Route("admin/functions")]
        [Authorize(Policy = PolicyNames.AdminAuthLevel)]
        public async Task<IActionResult> List()
        {
            return Ok(await _functionsManager.GetFunctionsMetadata(Request));
        }

        [HttpGet]
        [Route("admin/functions/{name}")]
        [Authorize(Policy = PolicyNames.AdminAuthLevel)]
        public async Task<IActionResult> Get(string name)
        {
            (var success, var function) = await _functionsManager.TryGetFunction(name, Request);

            return success
                ? Ok(function)
                : NotFound() as IActionResult;
        }

        [HttpPut]
        [Route("admin/functions/{name}")]
        [Authorize(Policy = PolicyNames.AdminAuthLevel)]
        public async Task<IActionResult> CreateOrUpdate(string name, [FromBody] FunctionMetadataResponse functionMetadata)
        {
            if (!FunctionNameValidationRegex.IsMatch(name))
            {
                return BadRequest($"{name} is not a valid function name");
            }

            (var success, var configChanged, var functionMetadataResponse) = await _functionsManager.CreateOrUpdate(name, functionMetadata, Request);

            if (success)
            {
                if (configChanged)
                {
                    // TODO: sync triggers
                }

                return Created(Request.GetDisplayUrl(), functionMetadataResponse);
            }
            else
            {
                return StatusCode(500);
            }
        }

        [HttpPost]
        [Route("admin/functions/{name}")]
        [Authorize(Policy = PolicyNames.AdminAuthLevel)]
        [RequiresRunningHost]
        public IActionResult Invoke(string name, [FromBody] FunctionInvocation invocation)
        {
            if (invocation == null)
            {
                return BadRequest();
            }

            FunctionDescriptor function = _scriptHostManager.Instance.GetFunctionOrNull(name);
            if (function == null)
            {
                return NotFound();
            }

            ParameterDescriptor inputParameter = function.Parameters.First(p => p.IsTrigger);
            Dictionary<string, object> arguments = new Dictionary<string, object>()
            {
                { inputParameter.Name, invocation.Input }
            };
            Task.Run(() => _scriptHostManager.Instance.CallAsync(function.Name, arguments));

            return Accepted();
        }

        [HttpGet]
        [Route("admin/functions/{name}/status")]
        [Authorize(Policy = PolicyNames.AdminAuthLevel)]
        [RequiresRunningHost]
        public IActionResult GetFunctionStatus(string name)
        {
            FunctionStatus status = new FunctionStatus();
            Collection<string> functionErrors = null;

            // first see if the function has any errors
            if (_scriptHostManager.Instance.FunctionErrors.TryGetValue(name, out functionErrors))
            {
                status.Errors = functionErrors;
            }
            else
            {
                // if we don't have any errors registered, make sure the function exists
                // before returning empty errors
                FunctionDescriptor function = _scriptHostManager.Instance.Functions.FirstOrDefault(p => p.Name.ToLowerInvariant() == name.ToLowerInvariant());
                if (function == null)
                {
                    return NotFound();
                }
            }

            return Ok(status);
        }

        [HttpDelete]
        [Route("admin/functions/{name}")]
        [Authorize(Policy = PolicyNames.AdminAuthLevel)]
        public async Task<IActionResult> Delete(string name)
        {
            (var found, var function) = await _functionsManager.TryGetFunction(name, Request);
            if (!found)
            {
                return NotFound();
            }

            (var deleted, var error) = _functionsManager.TryDeleteFunction(function);

            if (deleted)
            {
                return NoContent();
            }
            else
            {
                return StatusCode(StatusCodes.Status500InternalServerError, error);
            }
        }
    }
}