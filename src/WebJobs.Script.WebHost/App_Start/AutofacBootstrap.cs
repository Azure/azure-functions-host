// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System;
using System.Configuration;
using System.Threading.Tasks;
using System.Web.Configuration;
using System.Web.Hosting;
using Autofac;
using Microsoft.Azure.WebJobs.Script;
using WebJobs.Script.WebHost.WebHooks;

namespace WebJobs.Script.WebHost.App_Start
{
    public static class AutofacBootstrap
    {
        internal static void Initialize(ContainerBuilder builder, WebHostSettings settings)
        {
            ScriptHostConfiguration scriptHostConfig = new ScriptHostConfiguration()
            {
                RootScriptPath = settings.ScriptPath,
                RootLogPath = settings.LogPath,
                FileLoggingEnabled = true
            };

            // If running on Azure Web App, derive the host ID from the site name
            string hostId = Environment.GetEnvironmentVariable("WEBSITE_SITE_NAME");
            if (!String.IsNullOrEmpty(hostId))
            {
                // Truncate to the max host name length if needed
                const int MaximumHostIdLength = 32;
                if (hostId.Length > MaximumHostIdLength)
                {
                    hostId = hostId.Substring(0, MaximumHostIdLength);
                }

                scriptHostConfig.HostConfig.HostId = hostId.ToLowerInvariant();
            }

            WebScriptHostManager scriptHostManager = new WebScriptHostManager(scriptHostConfig);
            builder.RegisterInstance<WebScriptHostManager>(scriptHostManager);

            SecretManager secretManager = new SecretManager(settings.SecretsPath);
            // Make sure that host secrets get created on startup if they don't exist
            secretManager.GetHostSecrets();
            builder.RegisterInstance<SecretManager>(secretManager);

            WebHookReceiverManager webHookReceiverManager = new WebHookReceiverManager(secretManager);
            builder.RegisterInstance<WebHookReceiverManager>(webHookReceiverManager);

            if (!settings.IsSelfHost)
            {
                HostingEnvironment.QueueBackgroundWorkItem((ct) => scriptHostManager.RunAndBlock(ct));
            }
            else
            {
                Task.Run(() => scriptHostManager.RunAndBlock());
            }
        }
    }
}