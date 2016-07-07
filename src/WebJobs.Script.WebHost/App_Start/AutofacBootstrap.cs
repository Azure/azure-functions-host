// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System.Web.Http;
using Autofac;
using Autofac.Integration.WebApi;
using Microsoft.Azure.WebJobs.Host;
using Microsoft.Azure.WebJobs.Script.Config;
using Microsoft.Azure.WebJobs.Script.WebHost.WebHooks;

namespace Microsoft.Azure.WebJobs.Script.WebHost
{
    public static class AutofacBootstrap
    {
        internal static void Initialize(ScriptSettingsManager settingsManager, ContainerBuilder builder, WebHostSettings settings, HttpConfiguration config)
        {
            builder.RegisterInstance(settingsManager);

            builder.RegisterType<WebHostResolver>().SingleInstance();

            // these services are externally owned by the WebHostResolver, and will be disposed
            // when the resolver is disposed
            builder.RegisterType<DefaultSecretManagerFactory>().As<ISecretManagerFactory>().SingleInstance();
            builder.Register<TraceWriter>(ct => ct.ResolveOptional<WebScriptHostManager>()?.Instance?.TraceWriter ?? NullTraceWriter.Instance).ExternallyOwned();
            builder.Register<ISecretManager>(ct => ct.Resolve<WebHostResolver>().GetSecretManager(settings)).ExternallyOwned();
            builder.Register<WebScriptHostManager>(ct => ct.Resolve<WebHostResolver>().GetWebScriptHostManager(settings)).ExternallyOwned();
            builder.Register<WebHookReceiverManager>(ct => ct.Resolve<WebHostResolver>().GetWebHookReceiverManager(settings)).ExternallyOwned();
            builder.RegisterInstance(settings);
            builder.RegisterHttpRequestMessage(config);
        }
    }
}