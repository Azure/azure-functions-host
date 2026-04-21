// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Azure.WebJobs.Script.WebHost.Models;
using Microsoft.Azure.WebJobs.Script.WebHost.Security.Authorization.Policies;
using Microsoft.Azure.WebJobs.Script.Workers;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace Microsoft.Azure.WebJobs.Script.WebHost.Controllers
{
    /// <summary>
    /// Controller responsible for request-slot lease management.
    /// </summary>
    [Authorize(Policy = PolicyNames.AdminAuthLevel)]
    public class RequestSlotsController : Controller
    {
        private readonly ILogger _logger;

        public RequestSlotsController(ILoggerFactory loggerFactory)
        {
            _logger = loggerFactory.CreateLogger<RequestSlotsController>();
        }

        /// <summary>
        /// Reserves request slots against the runtime's shared pool.
        /// </summary>
        /// <remarks>
        /// Grants may be partial: the response's <see cref="RequestSlotsLeaseResponse.AcquiredSlotCount"/>
        /// indicates the number of slots actually reserved, which can be anywhere
        /// from zero to <see cref="RequestSlotsLeaseRequest.Count"/>.
        /// The caller is responsible for releasing whatever was granted.
        /// </remarks>
        [HttpPost]
        [Route("admin/request-slots/leases")]
        public IActionResult AcquireLeases([FromBody] RequestSlotsLeaseRequest request)
        {
            if (request is null)
            {
                return BadRequest("Request body is required.");
            }

            if (request.Count <= 0)
            {
                return BadRequest($"'{nameof(request.Count)}' must be greater than zero.");
            }

            var runtimeStateManager = HttpContext.RequestServices.GetService<IRuntimeStateManager>();
            if (runtimeStateManager is null)
            {
                _logger.LogWarning("AcquireLeases called but external workers feature is not enabled.");
                return StatusCode(StatusCodes.Status503ServiceUnavailable);
            }

            int granted = runtimeStateManager.AcquireSlots(request.Count);
            if (granted < request.Count)
            {
                _logger.LogInformation(
                    "Partial slot grant: requested {requested}, granted {granted}.",
                    request.Count,
                    granted);
            }

            return Ok(new RequestSlotsLeaseResponse { AcquiredSlotCount = granted });
        }

        /// <summary>
        /// Releases previously-acquired request slots back to the runtime's pool.
        /// </summary>
        /// <remarks>
        /// Release is best-effort: if <see cref="RequestSlotsLeaseRequest.Count"/>
        /// exceeds the runtime's current lease count, the runtime clamps at the
        /// actual leased amount and returns success. The caller (App Server) is
        /// expected to treat transport failures as non-fatal and continue to
        /// update its local accounting regardless.
        /// </remarks>
        [HttpDelete]
        [Route("admin/request-slots/leases")]
        public IActionResult ReleaseLeases([FromBody] RequestSlotsLeaseRequest request)
        {
            if (request is null)
            {
                return BadRequest("Request body is required.");
            }

            if (request.Count <= 0)
            {
                return BadRequest($"'{nameof(request.Count)}' must be greater than zero.");
            }

            var runtimeStateManager = HttpContext.RequestServices.GetService<IRuntimeStateManager>();
            if (runtimeStateManager is null)
            {
                _logger.LogWarning("ReleaseLeases called but external workers feature is not enabled.");
                return StatusCode(StatusCodes.Status503ServiceUnavailable);
            }

            runtimeStateManager.ReleaseSlots(request.Count);

            return Ok();
        }
    }
}
