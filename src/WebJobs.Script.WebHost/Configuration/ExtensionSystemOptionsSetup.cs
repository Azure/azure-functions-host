// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using Microsoft.Azure.WebJobs.Script.Configuration;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Options;

namespace Microsoft.Azure.WebJobs.Script.WebHost.Configuration;

public sealed class ExtensionSystemOptionsSetup : IConfigureNamedOptions<ExtensionSystemOptions>
{
    private const string SystemSectionName = "system";
    private readonly IConfiguration _configuration;

    public ExtensionSystemOptionsSetup(IConfiguration configuration)
    {
        _configuration = configuration;
    }

    public void Configure(string name, ExtensionSystemOptions options)
    {
        if (string.IsNullOrEmpty(name))
        {
            return;
        }

        string path = ConfigurationPath.Combine(ConfigurationSectionNames.JobHost, ConfigurationSectionNames.Extensions, name, SystemSectionName);
        IConfigurationSection section = _configuration.GetSection(path);
        section?.Bind(options);
        options.ExtensionName = name;
    }

    public void Configure(ExtensionSystemOptions options)
    {
        Configure(Options.DefaultName, options);
    }
}
