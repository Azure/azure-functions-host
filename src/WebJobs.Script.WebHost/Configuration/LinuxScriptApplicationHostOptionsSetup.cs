// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System;
using System.Threading;
using Microsoft.Azure.Storage.Blob;
using Microsoft.Extensions.Options;
using static Microsoft.Azure.WebJobs.Script.EnvironmentSettingNames;

namespace Microsoft.Azure.WebJobs.Script.WebHost.Configuration
{
    public class LinuxScriptApplicationHostOptionsSetup : IConfigureOptions<ScriptApplicationHostOptions>
    {
        private readonly IEnvironment _environment;
        private static readonly IConfigureOptions<ScriptApplicationHostOptions> _instance = new NullSetup();

        public LinuxScriptApplicationHostOptionsSetup(IEnvironment environment)
        {
            _environment = environment ?? throw new ArgumentNullException(nameof(environment));
        }

        internal static IConfigureOptions<ScriptApplicationHostOptions> NullInstance => _instance;

        public void Configure(ScriptApplicationHostOptions options)
        {
            options.IsFileSystemReadOnly = IsZipDeployment(options);
        }

        private bool IsZipDeployment(ScriptApplicationHostOptions options)
        {
            // If the app is using app settings for run from package, we don't need to check further, it must be a zip deployment.
            bool runFromPkgConfigured = Utility.IsValidZipSetting(_environment.GetEnvironmentVariable(AzureWebsiteZipDeployment)) ||
                Utility.IsValidZipSetting(_environment.GetEnvironmentVariable(AzureWebsiteAltZipDeployment)) ||
                Utility.IsValidZipSetting(_environment.GetEnvironmentVariable(AzureWebsiteRunFromPackage));

            if (runFromPkgConfigured)
            {
                return true;
            }

            // If SCM_RUN_FROM_PACKAGE is set to a valid value and the blob exists, it's a zip deployment.
            // We need to explicitly check if the blob exists because on Linux Consumption the app setting is always added, regardless if it's used or not.
            var url = _environment.GetEnvironmentVariable(ScmRunFromPackage);
            bool scmRunFromPkgConfigured = Utility.IsValidZipSetting(url) && BlobExists(url);
            options.IsScmRunFromPackage = scmRunFromPkgConfigured;

            return scmRunFromPkgConfigured;
        }

        public virtual bool BlobExists(string url)
        {
            if (string.IsNullOrEmpty(url))
            {
                return false;
            }

            try
            {
                CloudBlockBlob blob = new CloudBlockBlob(new Uri(url));

                int attempt = 0;
                while (true)
                {
                    try
                    {
                        return blob.Exists();
                    }
                    catch (Exception ex) when (!ex.IsFatal())
                    {
                        if (++attempt > 2)
                        {
                            return false;
                        }
                        Thread.Sleep(TimeSpan.FromSeconds(0.3));
                    }
                }
            }
            catch (Exception)
            {
                return false;
            }
        }

        private class NullSetup : IConfigureOptions<ScriptApplicationHostOptions>
        {
            public void Configure(ScriptApplicationHostOptions options)
            {
                // Do nothing.
            }
        }
    }
}
