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
            builder.Register<SecretManager>(ct => WebHostResolver.GetSecretManager(settings)).ExternallyOwned();
            builder.Register<WebScriptHostManager>(ct => WebHostResolver.GetWebScriptHostManager(settings)).ExternallyOwned();
            builder.Register<WebHookReceiverManager>(ct => WebHostResolver.GetWebHookReceiverManager(settings)).ExternallyOwned();
        }
    }
}