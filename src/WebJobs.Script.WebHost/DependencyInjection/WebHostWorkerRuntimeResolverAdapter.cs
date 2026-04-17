// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System;
using Microsoft.Azure.WebJobs.Script.Workers;
using Microsoft.Extensions.Configuration;

namespace Microsoft.Azure.WebJobs.Script.WebHost.DependencyInjection
{
    /// <summary>
    /// WebHost-level adapter for <see cref="IWorkerRuntimeResolver"/>.
    /// Always reads the worker runtime value from <see cref="IConfiguration"/>.
    /// </summary>
    internal sealed class WebHostWorkerRuntimeResolverAdapter : IWorkerRuntimeResolver
    {
        private readonly IConfiguration _configuration;

        public WebHostWorkerRuntimeResolverAdapter(IConfiguration configuration)
        {
            ArgumentNullException.ThrowIfNull(configuration);
            _configuration = configuration;
        }

        public string GetWorkerRuntime(string defaultValue = null)
        {
            var value = _configuration[EnvironmentSettingNames.FunctionWorkerRuntime];

            return !string.IsNullOrEmpty(value)
                ? value
                : defaultValue;
        }
    }
}
