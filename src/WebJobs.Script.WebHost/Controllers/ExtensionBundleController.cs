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
using Microsoft.Extensions.Logging;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace Microsoft.Azure.WebJobs.Script.WebHost.Controllers
{
    [Authorize(Policy = PolicyNames.AdminAuthLevel)]
    public class ExtensionBundleController : Controller
    {
        private readonly IExtensionBundleContentProvider _extensionBundleContent;

        public ExtensionBundleController(IExtensionBundleContentProvider extensionBundleContent)
        {
            _extensionBundleContent = extensionBundleContent ?? throw new ArgumentNullException(nameof(extensionBundleContent));
        }

        [HttpGet]
        [Route("admin/host/extensionBundle/v1/templates")]
        public async Task<IActionResult> GetTemplates()
        {
            string templates = await _extensionBundleContent.GetTemplates();

            if (string.IsNullOrEmpty(templates))
            {
                return NotFound(Resources.ExtensionBundleTemplatesNotFound);
            }

            var json = JsonConvert.DeserializeObject(templates);
            return Ok(json);
        }

        [HttpGet]
        [Route("admin/host/extensionBundle/v1/bindings")]
        public async Task<IActionResult> GetBindings()
        {
            string bindings = await _extensionBundleContent.GetBindings();

            if (string.IsNullOrEmpty(bindings))
            {
                return NotFound(Resources.ExtensionBundleBindingMetadataNotFound);
            }

            var json = JsonConvert.DeserializeObject(bindings);
            return Ok(json);
        }

        [HttpGet]
        [Route("admin/host/extensionBundle/v1/resources")]
        public async Task<IActionResult> GetResources()
        {
            string resources = await _extensionBundleContent.GetResources();

            if (string.IsNullOrEmpty(resources))
            {
                return NotFound(Resources.ExtensionBundleResourcesNotFound);
            }

            var json = JsonConvert.DeserializeObject(resources);
            return Ok(json);
        }

        [HttpGet]
        [Route("admin/host/extensionBundle/v1/resources.{locale}")]
        public async Task<IActionResult> GetResourcesLocale(string locale)
        {
            string resourceFileName = $"Resources.{locale}.json";
            var resources = await _extensionBundleContent.GetResources(resourceFileName);

            if (string.IsNullOrEmpty(resources))
            {
                return NotFound(string.Format(Resources.ExtensionBundleResourcesLocaleNotFound, locale));
            }

            var json = JsonConvert.DeserializeObject(resources);
            return Ok(json);
        }
    }
}
