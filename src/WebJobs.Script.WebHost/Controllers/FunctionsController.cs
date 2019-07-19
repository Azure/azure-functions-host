// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System.Collections.Generic;
using System.IO.Abstractions;
using System.IO.Compression;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.Extensions;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Azure.WebJobs.Extensions.Http;
using Microsoft.Azure.WebJobs.Script.Description;
using Microsoft.Azure.WebJobs.Script.Management.Models;
using Microsoft.Azure.WebJobs.Script.WebHost.Extensions;
using Microsoft.Azure.WebJobs.Script.WebHost.Filters;
using Microsoft.Azure.WebJobs.Script.WebHost.Management;
using Microsoft.Azure.WebJobs.Script.WebHost.Models;
using Microsoft.Azure.WebJobs.Script.WebHost.Security.Authorization.Policies;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Microsoft.Net.Http.Headers;

namespace Microsoft.Azure.WebJobs.Script.WebHost.Controllers
{
    /// <summary>
    /// Controller responsible for administrative and management operations on functions
    /// example retrieving a list of functions, invoking a function, creating a function, etc
    /// </summary>
    public class FunctionsController : Controller
    {
        private readonly IWebFunctionsManager _functionsManager;
        private readonly IWebJobsRouter _webJobsRouter;
        private readonly ILogger _logger;

        public FunctionsController(IWebFunctionsManager functionsManager, IWebJobsRouter webJobsRouter, ILoggerFactory loggerFactory)
        {
            _functionsManager = functionsManager;
            _webJobsRouter = webJobsRouter;
            _logger = loggerFactory?.CreateLogger(ScriptConstants.LogCategoryFunctionsController);
        }

        [HttpGet]
        [Route("admin/functions")]
        [Authorize(Policy = PolicyNames.AdminAuthLevel)]
        public async Task<IActionResult> List(bool includeProxies = false)
        {
            var result = await _functionsManager.GetFunctionsMetadata(includeProxies);
            return Ok(result);
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
            if (!Utility.IsValidFunctionName(name))
            {
                return BadRequest($"{name} is not a valid function name");
            }

            (var success, var configChanged, var functionMetadataResponse) = await _functionsManager.CreateOrUpdate(name, functionMetadata, Request);

            if (success)
            {
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
        public IActionResult Invoke(string name, [FromBody] FunctionInvocation invocation, [FromServices] IScriptJobHost scriptHost)
        {
            if (invocation == null)
            {
                return BadRequest();
            }

            FunctionDescriptor function = scriptHost.GetFunctionOrNull(name);
            if (function == null)
            {
                return NotFound();
            }

            ParameterDescriptor inputParameter = function.Parameters.First(p => p.IsTrigger);
            Dictionary<string, object> arguments = new Dictionary<string, object>()
            {
                { inputParameter.Name, invocation.Input }
            };

            Task.Run(async () =>
            {
                IDictionary<string, object> loggerScope = new Dictionary<string, object>
                {
                    { "MS_IgnoreActivity", null }
                };

                using (_logger.BeginScope(loggerScope))
                {
                    await scriptHost.CallAsync(function.Name, arguments);
                }
            });

            return Accepted();
        }

        [HttpGet]
        [Route("admin/functions/{name}/status")]
        [Authorize(Policy = PolicyNames.AdminAuthLevel)]
        public async Task<IActionResult> GetFunctionStatus(string name, [FromServices] IScriptJobHost scriptHost = null)
        {
            FunctionStatus status = new FunctionStatus();

            // first see if the function has any errors
            // if the host is not running or is offline
            // there will be no error info
            if (scriptHost != null &&
                scriptHost.FunctionErrors.TryGetValue(name, out ICollection<string> functionErrors))
            {
                status.Errors = functionErrors;
            }
            else
            {
                // if we don't have any errors registered, make sure the function exists
                // before returning empty errors
                var result = await _functionsManager.GetFunctionsMetadata(includeProxies: true);
                var function = result.FirstOrDefault(p => p.Name.ToLowerInvariant() == name.ToLowerInvariant());
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

        [HttpGet]
        [Route("admin/functions/download")]
        [Authorize(Policy = PolicyNames.AdminAuthLevel)]
        public IActionResult Download([FromServices] IOptions<ScriptApplicationHostOptions> webHostOptions)
        {
            var path = webHostOptions.Value.ScriptPath;
            var dirInfo = FileUtility.DirectoryInfoFromDirectoryName(path);
            return new FileCallbackResult(new MediaTypeHeaderValue("application/octet-stream"), async (outputStream, _) =>
            {
                using (var zipArchive = new ZipArchive(outputStream, ZipArchiveMode.Create))
                {
                    foreach (FileSystemInfoBase fileSysInfo in dirInfo.GetFileSystemInfos())
                    {
                        if (fileSysInfo is DirectoryInfoBase directoryInfo)
                        {
                            await zipArchive.AddDirectory(directoryInfo, fileSysInfo.Name);
                        }
                        else
                        {
                            // Add it at the root of the zip
                            await zipArchive.AddFile(fileSysInfo.FullName, string.Empty);
                        }
                    }
                }
            })
            {
                FileDownloadName = (System.Environment.GetEnvironmentVariable("WEBSITE_SITE_NAME") ?? "functions") + ".zip"
            };
        }
    }
}