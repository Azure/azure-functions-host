// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System;
using System.IO;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Azure.WebJobs.Script.WebHost.Management;
using Microsoft.Azure.WebJobs.Script.WebHost.Models;
using Microsoft.Azure.WebJobs.Script.WebHost.Security.Authorization.Policies;
using Microsoft.Extensions.Logging;

namespace Microsoft.Azure.WebJobs.Script.WebHost.Controllers
{
    /// <summary>
    /// Controller responsible for instance operations that are orthogonal to the script host.
    /// An instance is an unassigned generic container running with the runtime in standby mode.
    /// These APIs are used by the AppService Controller to validate standby instance status and info.
    /// </summary>
    public class InstanceController : Controller
    {
        private readonly IEnvironment _environment;
        private readonly IInstanceManager _instanceManager;
        private readonly ILogger _logger;
        private readonly StartupContextProvider _startupContextProvider;

        public InstanceController(IEnvironment environment, IInstanceManager instanceManager, ILoggerFactory loggerFactory, StartupContextProvider startupContextProvider)
        {
            _environment = environment;
            _instanceManager = instanceManager;
            _logger = loggerFactory.CreateLogger<InstanceController>();
            _startupContextProvider = startupContextProvider;
        }

        [HttpPost]
        [Route("admin/instance/filewriter")]
        public IActionResult WriteFileWithWriter()
        {
            try
            {
                var path = Path.Combine("/home", "site", "deployments", $"host-streamwriter.txt");
                using (var writer = System.IO.File.CreateText(path))
                {
                    writer.WriteLine("Test: Line One");
                    writer.WriteLine("Test: Line One");
                }
            }
            catch (Exception e)
            {
                return BadRequest(e);
            }

            return Ok();
        }

        [HttpPost]
        [Route("admin/instance/filewriteall")]
        public IActionResult WriteFileWithWriteAll()
        {
            try
            {
                var path = Path.Combine("/home", "site", "deployments", $"host-filewriteline.txt");
                System.IO.File.WriteAllText(path, "Hello World, this is a test");
            }
            catch (Exception e)
            {
                return BadRequest(e);
            }

            return Ok();
        }

        [HttpPost]
        [Route("admin/instance/filewritestream")]
        public IActionResult WriteFileWithStream()
        {
            try
            {
                var path = Path.Combine("/home", "site", "deployments", $"host-filestream.txt");
                using (FileStream destinationStream = System.IO.File.Create(path))
                {
                    byte[] bytes = System.Text.Encoding.UTF8.GetBytes("one two three");
                    destinationStream.Write(bytes, 0, bytes.Length);
                }
            }
            catch (Exception e)
            {
                return BadRequest(e);
            }

            return Ok();
        }

        [HttpPost]
        [Route("admin/instance/assign")]
        [Authorize(Policy = PolicyNames.AdminAuthLevel)]
        public async Task<IActionResult> Assign([FromBody] EncryptedHostAssignmentContext encryptedAssignmentContext)
        {
            _logger.LogDebug($"Starting container assignment for host : {Request?.Host}. ContextLength is: {encryptedAssignmentContext.EncryptedContext?.Length}");

            var assignmentContext = _startupContextProvider.SetContext(encryptedAssignmentContext);

            // before starting the assignment we want to perform as much
            // up front validation on the context as possible
            string error = await _instanceManager.ValidateContext(assignmentContext);
            if (error != null)
            {
                return StatusCode(StatusCodes.Status400BadRequest, error);
            }

            // Wait for Sidecar specialization to complete before returning ok.
            // This shouldn't take too long so ok to do this sequentially.
            error = await _instanceManager.SpecializeMSISidecar(assignmentContext);
            if (error != null)
            {
                return StatusCode(StatusCodes.Status500InternalServerError, error);
            }

            var succeeded = _instanceManager.StartAssignment(assignmentContext);

            return succeeded
                ? Accepted()
                : StatusCode(StatusCodes.Status409Conflict, "Instance already assigned");
        }

        [HttpGet]
        [Route("admin/instance/info")]
        [Authorize(Policy = PolicyNames.AdminAuthLevel)]
        public IActionResult GetInstanceInfo()
        {
            return Ok(_instanceManager.GetInstanceInfo());
        }

        [HttpGet]
        [Route("admin/instance/http-health")]
        public IActionResult GetHttpHealthStatus()
        {
            // Reaching here implies that http health of the container is ok.
            return Ok();
        }
    }
}
