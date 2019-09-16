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
using Microsoft.Azure.WebJobs.Script.ExtensionBundle;
using Microsoft.Azure.WebJobs.Script.Models;
using Microsoft.Azure.WebJobs.Script.Properties;
using Microsoft.Azure.WebJobs.Script.WebHost.Models;
using Microsoft.Azure.WebJobs.Script.WebHost.Security.Authorization.Policies;
using Microsoft.Extensions.Options;
using Newtonsoft.Json;

namespace Microsoft.Azure.WebJobs.Script.WebHost.Controllers
{
    [Authorize(Policy = PolicyNames.AdminAuthLevel)]
    public class ExtensionsController : Controller
    {
        private readonly IExtensionsManager _extensionsManager;
        private readonly ScriptSettingsManager _settingsManager;
        private readonly IExtensionBundleManager _extensionBundleManager;
        private readonly IEnvironment _environment;
        private readonly IOptions<ScriptApplicationHostOptions> _applicationHostOptions;

        public ExtensionsController(IExtensionsManager extensionsManager, ScriptSettingsManager settingsManager, IExtensionBundleManager extensionBundleManager, IEnvironment environment, IOptions<ScriptApplicationHostOptions> applicationHostOptions)
        {
            _extensionsManager = extensionsManager ?? throw new ArgumentNullException(nameof(extensionsManager));
            _settingsManager = settingsManager ?? throw new ArgumentNullException(nameof(settingsManager));
            _extensionBundleManager = extensionBundleManager ?? throw new ArgumentNullException(nameof(extensionBundleManager));
            _applicationHostOptions = applicationHostOptions ?? throw new ArgumentNullException(nameof(applicationHostOptions));
            _environment = environment;
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
        public Task<IActionResult> Post([FromBody]ExtensionPackageReferenceWithActions package) => InstallExtension(package);

        [HttpPut("{id}")]
        [Route("admin/host/extensions")]
        public Task<IActionResult> Put(int id, [FromBody]ExtensionPackageReferenceWithActions package) => InstallExtension(package);

        [HttpDelete]
        [Route("admin/host/extensions/{id}")]
        public async Task<IActionResult> Delete(string id)
        {
            if (_extensionBundleManager.IsExtensionBundleConfigured())
            {
                return BadRequest(Resources.ExtensionBundleBadRequestDelete);
            }

            if (!_environment.IsPersistentFileSystemAvailable())
            {
                return BadRequest(Resources.ErrorDeletingExtension);
            }

            // TODO: Check if we have an active job

            var job = await CreateJob(new ExtensionPackageReference() { Id = id, Version = string.Empty });
            await _extensionsManager.DeleteExtensions(id)
                .ContinueWith(t => ProcessJobTaskResult(t, job.Id));

            var apiModel = ApiModelUtility.CreateApiModel(job, Request, $"jobs/{job.Id}");
            string action = $"{Request.Scheme}://{Request.Host.ToUriComponent()}{Url.Action(nameof(GetJobs), "Extensions", new { id = job.Id })}{Request.QueryString}";
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

        [HttpGet]
        [Route("admin/host/extensions/jobs")]
        public async Task<IActionResult> GetJobs()
        {
            IEnumerable<ExtensionsRestoreJob> jobs = await GetInProgressJobs();

            var jobContent = new { jobs };
            var result = ApiModelUtility.CreateApiModel(jobContent, Request);

            return Ok(result);
        }

        public async Task<IActionResult> InstallExtension(ExtensionPackageReferenceWithActions package, bool verifyConflict = true)
        {
            if (package == null)
            {
                return BadRequest();
            }

            if (_extensionBundleManager.IsExtensionBundleConfigured())
            {
                return BadRequest(Resources.ExtensionBundleBadRequestInstall);
            }

            if (!_environment.IsPersistentFileSystemAvailable())
            {
                return BadRequest(Resources.ErrorInstallingExtension);
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

            ExtensionsRestoreJob job = await CreateJob(package);

            string jobId = job.Id;
            Enum.TryParse(package.PostInstallActions, true, out ExtensionPostInstallActions postInstallActions);
            var addTask = _extensionsManager.AddExtensions(package)
                .ContinueWith(t => ProcessJobTaskResult(t, jobId, postInstallActions));

            var apiModel = ApiModelUtility.CreateApiModel(job, Request, $"jobs/{job.Id}");
            string action = $"{Request.Scheme}://{Request.Host.ToUriComponent()}{Url.Action(nameof(GetJobs), "Extensions", new { id = job.Id })}{Request.QueryString}";
            return Accepted(action, apiModel);
        }

        private async Task ProcessJobTaskResult(Task jobTask, string jobId, ExtensionPostInstallActions postInstallActions = ExtensionPostInstallActions.None)
        {
            ExtensionsRestoreJob job = await GetJob(jobId);
            if (job == null)
            {
                return;
            }

            if (jobTask.IsFaulted)
            {
                job.Status = ExtensionsRestoreStatus.Failed;
                job.Error = jobTask.Exception.InnerException?.Message;
            }
            else
            {
                job.Status = ExtensionsRestoreStatus.Succeeded;
            }

            job.EndTime = DateTimeOffset.Now;

            if (postInstallActions.HasFlag(ExtensionPostInstallActions.BringAppOnline))
            {
                await FileMonitoringService.SetAppOfflineState(_applicationHostOptions.Value.ScriptPath, false);
            }

            await SaveJob(job);
        }

        private async Task<ExtensionsRestoreJob> CreateJob(ExtensionPackageReference package)
        {
            var job = new ExtensionsRestoreJob()
            {
                Properties = new Dictionary<string, string>()
                {
                    { "id", package.Id },
                    { "version", package.Version }
                }
            };

            await SaveJob(job);

            return job;
        }

        private async Task SaveJob(ExtensionsRestoreJob job)
        {
            string jobPath = Path.Combine(GetJobBasePath(), $"{job.Id}.json");
            Directory.CreateDirectory(Path.GetDirectoryName(jobPath));

            await FileUtility.WriteAsync(jobPath, JsonConvert.SerializeObject(job));
        }

        private string GetJobBasePath()
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

            FileUtility.EnsureDirectoryExists(basePath);
            return basePath;
        }

        private async Task<ExtensionsRestoreJob> GetJob(string jobId)
        {
            string path = Path.Combine(GetJobBasePath(), $"{jobId}.json");

            if (System.IO.File.Exists(path))
            {
                string json = await FileUtility.ReadAsync(path);

                return JsonConvert.DeserializeObject<ExtensionsRestoreJob>(json);
            }

            return null;
        }

        private async Task<IEnumerable<ExtensionsRestoreJob>> GetInProgressJobs()
        {
            var jobPaths = await FileUtility.GetFilesAsync(GetJobBasePath(), "*.json");
            List<ExtensionsRestoreJob> jobs = new List<ExtensionsRestoreJob>();

            foreach (var jobPath in jobPaths)
            {
                if (System.IO.File.Exists(jobPath))
                {
                    string json = await FileUtility.ReadAsync(jobPath);
                    var job = JsonConvert.DeserializeObject<ExtensionsRestoreJob>(json);
                    if (job.Status == ExtensionsRestoreStatus.Started)
                    {
                        jobs.Add(job);
                    }
                }
            }

            return jobs;
        }
    }
}
