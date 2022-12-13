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
    public class FunctionsHostingConfigSource : FileConfigurationSource
    {
        private readonly string _path;

        public FunctionsHostingConfigSource(IEnvironment environment)
        {
            // If runs on Linux SKUs read from FunctionsPlatformConfigFilePath
            _path = environment.GetEnvironmentVariable(EnvironmentSettingNames.FunctionsPlatformConfigFilePath);
            if (string.IsNullOrEmpty(_path))
            {
                // This path is for windows SKUs
                _path = Environment.ExpandEnvironmentVariables(System.IO.Path.Combine("%ProgramFiles(x86)%", "SiteExtensions", "kudu", "ScmHostingConfigurations.txt"));
            }
        }

        /// <summary>
        /// Builds the <see cref="FunctionsHostingConfigSource"/> for this source.
        /// </summary>
        /// <param name="builder">The <see cref="IConfigurationBuilder"/>.</param>
        /// <returns>A <see cref="FunctionsHostingConfigProvider"/></returns>
        public override IConfigurationProvider Build(IConfigurationBuilder builder)
        {
            Path = _path;
            Optional = true;
            ReloadOnChange = true;
            ResolveFileProvider();
            EnsureDefaults(builder);

            return new FunctionsHostingConfigProvider(this);
        }
    }
}
