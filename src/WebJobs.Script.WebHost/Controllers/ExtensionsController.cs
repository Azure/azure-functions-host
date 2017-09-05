// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Azure.WebJobs.Script.BindingExtensions;
using Microsoft.Azure.WebJobs.Script.Config;
using Microsoft.Azure.WebJobs.Script.Models;
using Microsoft.Azure.WebJobs.Script.WebHost.Models;
using Microsoft.Azure.WebJobs.Script.WebHost.Security.Authorization.Policies;
using Newtonsoft.Json;

namespace Microsoft.Azure.WebJobs.Script.WebHost.Controllers
{
    [Authorize(Policy = PolicyNames.AdminAuthLevel)]
    public class ExtensionsController : Controller
    {
        private readonly IExtensionsManager _extensionsManager;
        private readonly ScriptSettingsManager _settingsManager;

        public ExtensionsController(IExtensionsManager extensionsManager, ScriptSettingsManager settingsManager)
        {
            _extensionsManager = extensionsManager ?? throw new ArgumentNullException(nameof(extensionsManager));
            _settingsManager = settingsManager ?? throw new ArgumentNullException(nameof(settingsManager));
        }

        [HttpGet]
        [Route("admin/host/extensions")]
        public async Task<IActionResult> Get()
        {
            IEnumerable<ExtensionPackageReference> extensions = await _extensionsManager.GetExtensions();

            var extensionsContent = new
            {
                extensions = extensions
            };

            var result = ApiModelUtility.CreateApiModel(extensionsContent, Request);

            return Ok(result);
        }

        [HttpPost]
        [Route("admin/host/extensions")]
        public Task<IActionResult> Post([FromBody]ExtensionPackageReference package) => InstallExtension(package);

        [HttpPut("{id}")]
        [Route("admin/host/extensions")]
        public Task<IActionResult> Put(int id, [FromBody]ExtensionPackageReference package) => InstallExtension(package);

        [HttpDelete("{id}")]
        [Route("admin/host/extensions")]
        public async Task<IActionResult> Delete(string id)
        {
            // TODO: Check if we have an active job

            var job = await CreateJob();

            await _extensionsManager.DeleteExtensions(id)
                .ContinueWith(t => ProcessJobTaskResult(t, job.Id));

            var apiModel = ApiModelUtility.CreateApiModel(job, Request, $"jobs/{job.Id}");
            string action = $"{Request.Scheme}://{Request.Host.ToUriComponent()}/{Url.Action(nameof(GetJobs), "Extensions", new { id = job.Id })}";
            return Accepted(action, apiModel);
        }

        [HttpGet]
        [Route("admin/host/extensions/jobs/{id}")]
        public async Task<IActionResult> GetJobs(string id)
        {
            ExtensionsRestoreJob job = await GetJob(id);

            if (job == null)
            {
                return NotFound();
            }

            var apiModel = ApiModelUtility.CreateApiModel(job, Request);
            return Ok(apiModel);
        }

        public async Task<IActionResult> InstallExtension(ExtensionPackageReference package, bool verifyConflict = true)
        {
            if (package == null)
            {
                return BadRequest();
            }

            if (verifyConflict)
            {
                // If a different version of this extension is already installed, conflict:
                var extensions = await _extensionsManager.GetExtensions();
                if (extensions.Any(e => e.Id.Equals(package.Id) && !e.Version.Equals(package.Version)))
                {
                    return StatusCode(StatusCodes.Status409Conflict);
                }
            }

            ExtensionsRestoreJob job = await CreateJob();

            string jobId = job.Id;
            var addTask = _extensionsManager.AddExtensions(package)
                .ContinueWith(t => ProcessJobTaskResult(t, jobId));

            var apiModel = ApiModelUtility.CreateApiModel(job, Request, $"jobs/{job.Id}");
            string action = $"{Request.Scheme}://{Request.Host.ToUriComponent()}/{Url.Action(nameof(GetJobs), "Extensions", new { id = job.Id })}";
            return Accepted(action, apiModel);
        }

        private async Task ProcessJobTaskResult(Task jobTask, string jobId)
        {
            ExtensionsRestoreJob job = await GetJob(jobId);
            if (job == null)
            {
                return;
            }

            if (jobTask.IsFaulted)
            {
                job.Status = ExtensionRestoreStatus.Failed;
                job.Error = jobTask.Exception.InnerException?.Message;
            }
            else
            {
                job.Status = ExtensionRestoreStatus.Succeeded;
            }

            job.EndTime = DateTimeOffset.Now;

            await SaveJob(job);
        }

        private async Task<ExtensionsRestoreJob> CreateJob()
        {
            var job = new ExtensionsRestoreJob();
            await SaveJob(job);

            return job;
        }

        private async Task SaveJob(ExtensionsRestoreJob job)
        {
            string jobPath = GetJobPath(job.Id);

            Directory.CreateDirectory(Path.GetDirectoryName(jobPath));

            await FileUtility.WriteAsync(jobPath, JsonConvert.SerializeObject(job));
        }

        private string GetJobPath(string jobId)
        {
            string home = _settingsManager.GetSetting(EnvironmentSettingNames.AzureWebsiteHomePath);
            string basePath = null;
            if (!string.IsNullOrEmpty(home))
            {
                basePath = Path.Combine(home, "data", "Functions", "extensions");
            }
            else
            {
                basePath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".azurefunctions", "extensions");
            }

            return Path.Combine(basePath, $"{jobId}.json");
        }

        private async Task<ExtensionsRestoreJob> GetJob(string jobId)
        {
            string path = GetJobPath(jobId);

            if (System.IO.File.Exists(path))
            {
                string json = await FileUtility.ReadAsync(path);

                return JsonConvert.DeserializeObject<ExtensionsRestoreJob>(json);
            }

            return null;
        }
    }
}
