// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System;
using System.Collections.Generic;
using Microsoft.Azure.WebJobs.Extensions.Http;
using Microsoft.Azure.WebJobs.Script.WebHost.Configuration;
using Microsoft.Extensions.Configuration;
using Microsoft.WebJobs.Script.Tests;
using Moq;
using Xunit;

namespace Microsoft.Azure.WebJobs.Script.Tests.Configuration;

[Trait(TestTraits.Group, TestTraits.WebhookTests)]
public class ExtensionsSystemOptionsSetupTests
{
    [Theory]
    [InlineData("mcp", AuthorizationLevel.Anonymous)]
    [InlineData("eventGrid", AuthorizationLevel.System)]
    [InlineData("myext", AuthorizationLevel.System)]
    public void Configure_BindsNamedOptions(string extensionName, AuthorizationLevel expectedAuthorizationLevel)
    {
        var inMemorySettings = new Dictionary<string, string>
        {
            ["AzureFunctionsJobHost:extensions:mcp:system:webhookAuthorizationLevel"] = "Anonymous",
            ["AzureFunctionsJobHost:extensions:eventGrid:system:webhookAuthorizationLevel"] = "System",
            ["AzureFunctionsJobHost:extensions:myext:someOtherKey"] = "shouldBeIgnored"
        };

        var config = new ConfigurationBuilder()
            .AddInMemoryCollection(inMemorySettings)
            .Build();

        var setup = new ExtensionSystemOptionsSetup(config);

        var options = new ExtensionSystemOptions();
        setup.Configure(extensionName, options);

        Assert.Equal(extensionName, options.ExtensionName);
        Assert.Equal(expectedAuthorizationLevel, options.WebhookAuthorizationLevel);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    public void Configure_NameNullOrEmpty_ReturnsDefaultOptions(string name)
    {
        var inMemorySettings = new Dictionary<string, string>
        {
            ["AzureFunctionsJobHost:extensions:mcp:system:webhookAuthorizationLevel"] = "Anonymous",
            ["AzureFunctionsJobHost:extensions:eventGrid:system:webhookAuthorizationLevel"] = "System",
            ["AzureFunctionsJobHost:extensions:myext:someOtherKey"] = "shouldBeIgnored"
        };

        var config = new ConfigurationBuilder()
            .AddInMemoryCollection(inMemorySettings)
            .Build();

        var setup = new ExtensionSystemOptionsSetup(config);

        var options = new ExtensionSystemOptions();
        setup.Configure(name, options);

        VerifyDefaults(options);
    }

    [Fact]
    public void Configure_NoNameOverload_ReturnsDefaultOptions()
    {
        var configMock = new Mock<IConfiguration>();
        var setup = new ExtensionSystemOptionsSetup(configMock.Object);

        var options = new ExtensionSystemOptions();
        setup.Configure(options);

        VerifyDefaults(options);
    }

    private void VerifyDefaults(ExtensionSystemOptions options)
    {
        Assert.Null(options.ExtensionName);
        Assert.Equal(AuthorizationLevel.System, options.WebhookAuthorizationLevel);
    }
}