// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System;
using System.Collections.Generic;
using System.Threading;
using Azure.Functions.Host.Models;
using Azure.Functions.Host.WorkerScaleIn;
using Azure.Functions.Rpc.Client;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Azure.WebJobs.Script.Description;
using Microsoft.Azure.WebJobs.Script.WebHost.Security.Authorization.Policies;
using Microsoft.Extensions.Logging;

namespace Azure.Functions.Host.Controllers;

/// <summary>
/// Provides count-only non-HTTP worker scale-in for compute separation mode.
/// </summary>
public sealed partial class WorkerScaleInController : Controller
{
    private readonly ILogger<WorkerScaleInController> _logger;
    private readonly IWorkerChannelRegistry _registry;

    /// <summary>
    /// Initializes a new instance of the <see cref="WorkerScaleInController"/> class.
    /// </summary>
    /// <param name="logger">The logger.</param>
    /// <param name="registry">The registry that owns worker selection and drain commitment.</param>
    public WorkerScaleInController(
        ILogger<WorkerScaleInController> logger,
        IWorkerChannelRegistry registry)
    {
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _registry = registry ?? throw new ArgumentNullException(nameof(registry));
    }

    /// <summary>
    /// Commits excess matching workers to drain without waiting for drain completion.
    /// </summary>
    /// <param name="request">The exact group, capacity class, and absolute retained count.</param>
    /// <param name="cancellationToken">The request cancellation token.</param>
    /// <returns>The number of workers newly committed to drain.</returns>
    [HttpPost]
    [Route("admin/workers/scale-in")]
    [Authorize(Policy = PolicyNames.AdminAuthLevel)]
    public IActionResult ScaleInWorkers(
        [FromBody] WorkerScaleInRequest? request,
        CancellationToken cancellationToken = default)
    {
        IReadOnlyList<RequestValidationError> errors = ValidateRequest(request);
        if (errors.Count > 0)
        {
            return BadRequest(new RequestValidationResponse(errors));
        }

        cancellationToken.ThrowIfCancellationRequested();
        string functionGroup = request!.FunctionGroup!;
        bool isAlwaysReady = request.IsAlwaysReady!.Value;
        int desiredWorkerCount = request.DesiredWorkerCount!.Value;
        WorkerScaleInResult result = _registry.ScaleInWorkers(
            functionGroup,
            isAlwaysReady,
            desiredWorkerCount);

        Log.ScaleInCommitted(
            _logger,
            functionGroup,
            isAlwaysReady,
            desiredWorkerCount,
            result.NewlyDrainingWorkerCount);

        return Ok(new WorkerScaleInResponse(result.NewlyDrainingWorkerCount));
    }

    private IReadOnlyList<RequestValidationError> ValidateRequest(WorkerScaleInRequest? request)
    {
        List<RequestValidationError> errors = [];
        if (!ModelState.IsValid || request is null)
        {
            errors.Add(new(ErrorCodes.InvalidBody, "request"));
            return errors;
        }

        if (string.IsNullOrWhiteSpace(request.FunctionGroup))
        {
            errors.Add(new(ErrorCodes.Required, "functionGroup"));
        }
        else if (string.Equals(request.FunctionGroup, FunctionGroups.Http, StringComparison.OrdinalIgnoreCase))
        {
            errors.Add(new(ErrorCodes.InvalidValue, "functionGroup"));
        }

        if (request.IsAlwaysReady is null)
        {
            errors.Add(new(ErrorCodes.Required, "isAlwaysReady"));
        }

        if (request.DesiredWorkerCount is null)
        {
            errors.Add(new(ErrorCodes.Required, "desiredWorkerCount"));
        }
        else if (request.DesiredWorkerCount < 0)
        {
            errors.Add(new(ErrorCodes.InvalidValue, "desiredWorkerCount"));
        }

        return errors;
    }

    private static class ErrorCodes
    {
        public const string InvalidBody = nameof(InvalidBody);
        public const string InvalidValue = nameof(InvalidValue);
        public const string Required = nameof(Required);
    }

    private static partial class Log
    {
        [LoggerMessage(710, LogLevel.Information,
            "Worker scale-in for function group {FunctionGroup}, always-ready class {IsAlwaysReady}, and desired count {DesiredWorkerCount} newly committed {NewlyDrainingWorkerCount} workers.")]
        public static partial void ScaleInCommitted(
            ILogger logger,
            string functionGroup,
            bool isAlwaysReady,
            int desiredWorkerCount,
            int newlyDrainingWorkerCount);
    }
}
