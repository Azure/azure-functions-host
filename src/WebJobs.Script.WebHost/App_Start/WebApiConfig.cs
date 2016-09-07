// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System;
using System.Collections.Generic;
using System.IO;
using System.Web;
using System.Web.Http;
using Autofac;
using Autofac.Integration.WebApi;
using Microsoft.Azure.WebJobs.Script.WebHost.Controllers;
using Microsoft.Azure.WebJobs.Script.WebHost.Handlers;

namespace Microsoft.Azure.WebJobs.Script.WebHost
{
    public static class WebApiConfig
    {
        public static void Register(HttpConfiguration config)
        {
            Register(config, GetDefaultSettings());
        }

        public static void Register(HttpConfiguration config, WebHostSettings settings = null, Action<ContainerBuilder, WebHostSettings> dependencyCallback = null)
        {
            if (config == null)
            {
                throw new ArgumentNullException("config");
            }
            if (settings == null)
            {
                throw new ArgumentNullException("settings");
            }

            // Delete hostingstart.html if any. Azure creates that in all sites by default
            string hostingStart = Path.Combine(settings.ScriptPath, "hostingstart.html");
            if (File.Exists(hostingStart))
            {
                File.Delete(hostingStart);
            }

            // Add necessary folders to the %PATH%
            PrependFoldersToEnvironmentPath();

            var builder = new ContainerBuilder();
            builder.RegisterApiControllers(typeof(FunctionsController).Assembly);
            AutofacBootstrap.Initialize(builder, settings);

            // Invoke registration callback
            dependencyCallback?.Invoke(builder, settings);

            var container = builder.Build();
            config.DependencyResolver = new AutofacWebApiDependencyResolver(container);

            config.MessageHandlers.Add(new WebScriptHostHandler(config));

            // Web API configuration and services

            // Web API routes
            config.MapHttpAttributeRoutes();

            config.Routes.MapHttpRoute(
                name: "Home",
                routeTemplate: string.Empty,
                defaults: new { controller = "Home" });

            config.Routes.MapHttpRoute(
                name: "Functions",
                routeTemplate: "{*uri}",
                defaults: new { controller = "Functions" });

            // Initialize WebHook Receivers
            config.InitializeReceiveGenericJsonWebHooks();
            config.InitializeReceiveAzureAlertWebHooks();
            config.InitializeReceiveKuduWebHooks();
            config.InitializeReceivePusherWebHooks();
            config.InitializeReceiveStripeWebHooks();
            config.InitializeReceiveTrelloWebHooks();
            config.InitializeReceiveDynamicsCrmWebHooks();
            config.InitializeReceiveMailChimpWebHooks();
            config.InitializeReceiveSlackWebHooks();
            config.InitializeReceiveBitbucketWebHooks();
            config.InitializeReceiveDropboxWebHooks();
            config.InitializeReceiveWordPressWebHooks();
            config.InitializeReceiveGitHubWebHooks();
            config.InitializeReceiveSalesforceWebHooks();
        }

        private static void PrependFoldersToEnvironmentPath()
        {
            // Only do this when %HOME% is defined (normally on Azure)
            string home = Environment.GetEnvironmentVariable(EnvironmentSettingNames.AzureWebsiteHomePath);
            if (!string.IsNullOrEmpty(home))
            {
                // Create the tools folder if it doesn't exist
                string toolsPath = Path.Combine(home, @"site\tools");
                Directory.CreateDirectory(toolsPath);

                var folders = new List<string>();
                folders.Add(Path.Combine(home, @"site\tools"));

                string path = Environment.GetEnvironmentVariable("PATH");
                string additionalPaths = String.Join(";", folders);

                // Make sure we haven't already added them. This can happen if the appdomain restart (since it's still same process)
                if (!path.Contains(additionalPaths))
                {
                    path = additionalPaths + ";" + path;

                    Environment.SetEnvironmentVariable("PATH", path);
                }
            }
        }

        private static WebHostSettings GetDefaultSettings()
        {
            WebHostSettings settings = new WebHostSettings();

            string home = Environment.GetEnvironmentVariable(EnvironmentSettingNames.AzureWebsiteHomePath);
            bool isLocal = string.IsNullOrEmpty(home);
            if (isLocal)
            {
                settings.ScriptPath = Environment.GetEnvironmentVariable(EnvironmentSettingNames.AzureWebJobsScriptRoot);
                settings.LogPath = Path.Combine(Path.GetTempPath(), @"Functions");
                settings.SecretsPath = HttpContext.Current.Server.MapPath("~/App_Data/Secrets");
            }
            else
            {
                // we're running in Azure
                settings.ScriptPath = Path.Combine(home, @"site\wwwroot");
                settings.LogPath = Path.Combine(home, @"LogFiles\Application\Functions");
                settings.SecretsPath = Path.Combine(home, @"data\Functions\secrets");
            }

            if (string.IsNullOrEmpty(settings.ScriptPath))
            {
                throw new InvalidOperationException("Unable to determine function script root directory.");
            }

            return settings;
        }
    }
}