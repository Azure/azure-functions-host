// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Configuration.Json;

namespace Microsoft.Azure.WebJobs.Script.Config
{
    /// <summary>
    /// Represents a JSON file as an <see cref="IConfigurationSource"/>.
    /// </summary>
    public class FunctionsHostingEnvironmentConfigSource : JsonConfigurationSource
    {
        private readonly string _path;

        public FunctionsHostingEnvironmentConfigSource(IEnvironment environment)
        {
            _path = environment.GetEnvironmentVariable(EnvironmentSettingNames.FunctionsHostingEnvironmentConfigFilePath);
        }

        public override IConfigurationProvider Build(IConfigurationBuilder builder)
        {
            Path = _path;
            Optional = true;
            ResolveFileProvider();
            EnsureDefaults(builder);
            return new FunctionsHostingEnvironmentConfigProvider(this);
        }
    }
}
