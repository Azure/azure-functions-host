// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using Autofac;
using Microsoft.Azure.WebJobs.Script.WebHost.WebHooks;

namespace Microsoft.Azure.WebJobs.Script.WebHost
{
    public static class AutofacBootstrap
    {
        internal static void Initialize(ContainerBuilder builder, WebHostSettings settings)
        {
            // register the resolver so that it is disposed when the container
            // is disposed
            var webHostResolver = new WebHostResolver();
            builder.RegisterInstance(webHostResolver);

            // these services are externally owned by the WebHostResolver, and will be disposed
            // when the resolver is disposed
            builder.Register<SecretManager>(ct => webHostResolver.GetSecretManager(settings)).ExternallyOwned();
            builder.Register<WebScriptHostManager>(ct => webHostResolver.GetWebScriptHostManager(settings)).ExternallyOwned();
            builder.Register<WebHookReceiverManager>(ct => webHostResolver.GetWebHookReceiverManager(settings)).ExternallyOwned();
        }
    }
}