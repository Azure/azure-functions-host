// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System.Net.Http;
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
            // register the resolver so that it is disposed when the container
            // is disposed
            var webHostResolver = new WebHostResolver();
            builder.RegisterInstance(webHostResolver);

            // these services are externally owned by the WebHostResolver, and will be disposed
            // when the resolver is disposed
            builder.Register<SecretManager>(ct => webHostResolver.GetSecretManager(settings)).ExternallyOwned();
            builder.Register<WebScriptHostManager>(ct => webHostResolver.GetWebScriptHostManager(settings)).ExternallyOwned();
            builder.Register<WebHookReceiverManager>(ct => webHostResolver.GetWebHookReceiverManager(settings)).ExternallyOwned();
            builder.RegisterInstance(settings);
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