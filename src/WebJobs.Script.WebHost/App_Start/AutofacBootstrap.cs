// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System;
using System.Net.Http;
using System.Threading.Tasks;
using System.Web.Hosting;
using System.Web.Http;
using Autofac;
using Autofac.Integration.WebApi;
using Microsoft.Azure.WebJobs.Script.WebHost.Kudu;
using Microsoft.Azure.WebJobs.Script.WebHost.WebHooks;

namespace Microsoft.Azure.WebJobs.Script.WebHost
{
    public static class AutofacBootstrap
    {
        internal static void Initialize(ContainerBuilder builder, WebHostSettings settings, HttpConfiguration config)
        {
            ScriptHostConfiguration scriptHostConfig = new ScriptHostConfiguration()
            {
                RootScriptPath = settings.ScriptPath,
                RootLogPath = settings.LogPath,
                FileLoggingEnabled = true,
                TraceWriter = settings.TraceWriter,
            };

            if (settings.IsSelfHost && settings.TraceWriter != null)
            {
                scriptHostConfig.HostConfig.Tracing.ConsoleLevel = System.Diagnostics.TraceLevel.Off;
            }

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

                // Trim any trailing - as they can cause problems with queue names
                hostId = hostId.TrimEnd('-');

                scriptHostConfig.HostConfig.HostId = hostId.ToLowerInvariant();
            }
            else if (settings.IsSelfHost)
            {
                // TODO: persist
                scriptHostConfig.HostConfig.HostId = Guid.NewGuid().ToString().Replace("-", "").ToLowerInvariant();
            }

            builder.RegisterInstance<WebHostSettings>(settings);

            SecretManager secretManager = new SecretManager(settings.SecretsPath);
            // Make sure that host secrets get created on startup if they don't exist
            secretManager.GetHostSecrets();
            builder.RegisterInstance<SecretManager>(secretManager);

            WebScriptHostManager scriptHostManager = new WebScriptHostManager(scriptHostConfig, secretManager);
            builder.RegisterInstance<WebScriptHostManager>(scriptHostManager);

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

            RegisterTypes(builder, config, settings);
        }

        private static void RegisterTypes(ContainerBuilder builder, HttpConfiguration config, WebHostSettings settings)
        {
            builder.RegisterHttpRequestMessage(config);

            builder.RegisterType<FunctionsManager>()
               .As<IFunctionsManager>()
               .InstancePerRequest();

            builder.Register(c => new KuduEnvironment(settings, c.Resolve<HttpRequestMessage>()))
                .As<IEnvironment>()
                .InstancePerRequest();

            builder.Register(c => ConsoleTracer.Instance)
                .As<ITracer>()
                .SingleInstance();
        }
    }
}