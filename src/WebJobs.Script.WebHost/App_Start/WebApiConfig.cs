// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System;
using System.Web.Http;
using System.Web.Http.ExceptionHandling;
using Autofac;
using Autofac.Integration.WebApi;
using Microsoft.Azure.WebJobs.Script.Config;
using Microsoft.Azure.WebJobs.Script.WebHost.Controllers;
using Microsoft.Azure.WebJobs.Script.WebHost.Handlers;

namespace Microsoft.Azure.WebJobs.Script.WebHost
{
    public static class WebApiConfig
    {
        public static void Initialize(HttpConfiguration config, ScriptSettingsManager settingsManager = null,
            WebHostSettings settings = null, Action<ContainerBuilder, WebHostSettings> dependencyCallback = null)
        {
            Register(config, settingsManager, settings, dependencyCallback);

            var scriptHostManager = config.DependencyResolver.GetService<WebScriptHostManager>();
            if (scriptHostManager != null && !scriptHostManager.Initialized)
            {
                scriptHostManager.Initialize();
            }
        }

        public static void Register(HttpConfiguration config, ScriptSettingsManager settingsManager = null,
            WebHostSettings settings = null, Action<ContainerBuilder, WebHostSettings> dependencyCallback = null)
        {
            if (config == null)
            {
                throw new ArgumentNullException("config");
            }

            settingsManager = settingsManager ?? ScriptSettingsManager.Instance;
            settings = settings ?? WebHostSettings.CreateDefault(settingsManager);

            var builder = new ContainerBuilder();
            builder.RegisterApiControllers(typeof(FunctionsController).Assembly);
            AutofacBootstrap.Initialize(settingsManager, builder, settings);

            // Invoke registration callback
            dependencyCallback?.Invoke(builder, settings);

            var container = builder.Build();
            config.DependencyResolver = new AutofacWebApiDependencyResolver(container);
            config.Formatters.Add(new PlaintextMediaTypeFormatter());
            config.Services.Replace(typeof(IExceptionHandler), new ExceptionProcessingHandler(config));
            AddMessageHandlers(config);

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

        private static void AddMessageHandlers(HttpConfiguration config)
        {
            config.MessageHandlers.Add(new WebScriptHostHandler(config));
            config.MessageHandlers.Add(new SystemTraceHandler(config));
        }
    }
}