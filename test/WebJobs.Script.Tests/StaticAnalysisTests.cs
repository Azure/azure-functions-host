// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information.

using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Azure.WebJobs.Script.WebHost;
using Microsoft.Azure.WebJobs.Script.WebHost.Controllers;
using Microsoft.Azure.WebJobs.Script.WebHost.Filters;
using Xunit;

namespace Microsoft.Azure.WebJobs.Script.Tests
{
    public class StaticAnalysisTests
    {
        /// <summary>
        /// This test exists to ensure we don't add any host APIs that return secrets via GET,
        /// unless they're marked with the <see cref="ResourceContainsSecretsAttribute"/>.
        /// </summary>
        /// <remarks>
        /// See <see cref="ArmExtensionResourceFilter"/> for details.
        /// </remarks>
        [Fact]
        public void VerifyHostReadApis()
        {
            // following is the set of GET APIs that don't return secrets
            var safeReaderApis = new string[]
            {
                "ExtensionBundleController.GetBindings",
                "ExtensionBundleController.GetResources",
                "ExtensionBundleController.GetResourcesLocale",
                "ExtensionBundleController.GetTemplates",
                "ExtensionsController.Get",
                "ExtensionsController.GetJobs",
                "ExtensionsController.GetJobs",
                "FunctionsController.Download",
                "FunctionsController.Get",
                "FunctionsController.GetFunctionStatus",
                "FunctionsController.List",
                "HostController.DrainStatus",
                "HostController.GetConfig",
                "HostController.GetHostStatus",
                "HostController.GetWorkerProcesses",
                "HostController.Ping",
                "InstanceController.GetHttpHealthStatus",
                "InstanceController.GetInstanceInfo"
            };

            // looking for all GET actions that aren't marked with the ResourceContainsSecretsAttribute
            var methodInfos = typeof(HostController).Assembly.GetTypes().Where(p => typeof(Controller).IsAssignableFrom(p)).SelectMany(type => type.GetMethods())
                .Where(method => method.IsPublic && method.IsDefined(typeof(HttpGetAttribute)) && !method.IsDefined(typeof(NonActionAttribute)) && Utility.GetHierarchicalAttributeOrNull<ResourceContainsSecretsAttribute>(method) == null).ToArray();
            var methodNames = methodInfos.Select(p => $"{p.DeclaringType.Name}.{p.Name}").OrderBy(p => p).ToArray();

            // if this check is failing, it means you've added new host GET API. If the API doesn't return secrets (i.e. is safe for an ARM Reader),
            // add it to the list above. If the API returns secrets, apply the ResourceContainsSecretsAttribute to the action method.
            Assert.Equal(safeReaderApis, methodNames);
        }
    }
}
