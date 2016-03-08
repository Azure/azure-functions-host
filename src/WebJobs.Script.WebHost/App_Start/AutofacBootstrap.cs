// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

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

            // If there is an explicit machine key, it makes a good default host id. It can still be
            // overridden in host.json
            var section = (MachineKeySection)ConfigurationManager.GetSection("system.web/machineKey");
            if (section.Decryption != "Auto" && section.ValidationKey.Length >= 32)
            {
                scriptHostConfig.HostConfig.HostId = section.ValidationKey.Substring(0, 32).ToLowerInvariant();
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