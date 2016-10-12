// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using Autofac;
using Microsoft.Azure.WebJobs.Script.Config;
using Microsoft.Azure.WebJobs.Script.WebHost.WebHooks;

namespace Microsoft.Azure.WebJobs.Script.WebHost
{
    public static class AutofacBootstrap
    {
        internal static void Initialize(ScriptSettingsManager settingsManager, ContainerBuilder builder, WebHostSettings settings)
        {
            builder.RegisterType<ScriptSettingsManager>().SingleInstance();

            // register the resolver so that it is disposed when the container
            // is disposed
            var webHostResolver = new WebHostResolver(settingsManager);
            builder.RegisterInstance(webHostResolver);

            // these services are externally owned by the WebHostResolver, and will be disposed
            // when the resolver is disposed
            builder.Register<ISecretManager>(ct => webHostResolver.GetSecretManager(settings)).ExternallyOwned();
            builder.Register<WebScriptHostManager>(ct => webHostResolver.GetWebScriptHostManager(settings)).ExternallyOwned();
            builder.Register<WebHookReceiverManager>(ct => webHostResolver.GetWebHookReceiverManager(settings)).ExternallyOwned();
        }
    }
}