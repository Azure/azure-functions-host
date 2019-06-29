// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System;
using System.IO;
using System.Threading.Tasks;
using Microsoft.Azure.WebJobs.Script.Diagnostics.Extensions;
using Microsoft.Azure.WebJobs.Script.Properties;
using Microsoft.Extensions.Logging;

namespace Microsoft.Azure.WebJobs.Script.ExtensionBundle
{
    public class ExtensionBundleContentProvider : IExtensionBundleContentProvider
    {
        private readonly IExtensionBundleManager _extensionBundleManager;
        private readonly ILogger _logger;

        public ExtensionBundleContentProvider(IExtensionBundleManager extensionBundleManager, ILogger<ExtensionBundleContentProvider> logger)
        {
            _extensionBundleManager = extensionBundleManager;
            _logger = logger;
        }

        public async Task<string> GetTemplates() => await GetFileContent(Path.Combine("StaticContent", "v1", "templates", ScriptConstants.ExtensionBundleTemplatesFile));

        public async Task<string> GetBindings() => await GetFileContent(Path.Combine("StaticContent", "v1", "bindings", ScriptConstants.ExtensionBundleBindingMetadataFile));

        public async Task<string> GetResources(string fileName = null) => await GetFileContent(Path.Combine("StaticContent", "v1", "resources", fileName ?? ScriptConstants.ExtensionBundleResourcesFile));

        public async Task<string> GetFileContent(string path)
        {
            if (!_extensionBundleManager.IsExtensionBundleConfigured())
            {
                _logger.ContentProviderNotConfigured(path);
                return null;
            }

            var bundlePath = await _extensionBundleManager.GetExtensionBundlePath();
            string contentFilePath = Path.Combine(bundlePath, path);

            if (FileUtility.FileExists(contentFilePath))
            {
                return await FileUtility.ReadAsync(contentFilePath);
            }
            else
            {
                _logger.ContentFileNotFound(contentFilePath);
                return null;
            }
        }
    }
}
