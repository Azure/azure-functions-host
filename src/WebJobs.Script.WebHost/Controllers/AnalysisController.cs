// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System.Collections.Generic;
using System.Threading;
using DotNetTI.BreakingChangeAnalysis;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Azure.WebJobs.Script.ChangeAnalysis;
using Microsoft.Azure.WebJobs.Script.WebHost.Security.Authorization.Policies;

namespace WebJobs.Script.WebHost.Controllers
{
    public class AnalysisController : Controller
    {
        private readonly IBreakingChangeAnalysisService _changeAnalysisService;

        public AnalysisController(IBreakingChangeAnalysisService changeAnalysisService)
        {
            _changeAnalysisService = changeAnalysisService;
        }

        [HttpGet]
        [HttpPost]
        [Route("admin/host/compatibilityreport")]
        [Authorize(Policy = PolicyNames.AdminAuthLevelOrInternal)]
        [ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
        public IActionResult AnalyzeBreakingChanges()
        {
            IEnumerable<AssemblyReport> reports = _changeAnalysisService.LogBreakingChangeReport(CancellationToken.None);

            return Ok(reports);
        }
    }
}