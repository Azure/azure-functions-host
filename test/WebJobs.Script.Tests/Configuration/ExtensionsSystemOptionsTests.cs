// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System;
using Microsoft.Azure.WebJobs.Extensions.Http;
using Microsoft.Azure.WebJobs.Script.WebHost.Configuration;
using Microsoft.WebJobs.Script.Tests;
using Newtonsoft.Json.Linq;
using Xunit;

namespace Microsoft.Azure.WebJobs.Script.Tests.Configuration;

[Trait(TestTraits.Group, TestTraits.WebhookTests)]
public class ExtensionsSystemOptionsTests
{
    [Fact]
    public void Format_ReturnsExpectedJson()
    {
        var options = new ExtensionSystemOptions
        {
            ExtensionName = "myext",
            WebhookAuthorizationLevel = AuthorizationLevel.Anonymous
        };

        var formatted = options.Format();
        var parsed = JObject.Parse(formatted);

        Assert.Equal(2, parsed.Count);
        Assert.Equal("myext", parsed["extensionName"]);
        Assert.Equal("Anonymous", parsed["webhookAuthorizationLevel"]);
    }

    [Theory]
    [InlineData(AuthorizationLevel.System)]
    [InlineData(AuthorizationLevel.Anonymous)]
    public void WebhookAuthorizationLevel_Set_ValidValues_DoesNotThrow(AuthorizationLevel level)
    {
        var options = new ExtensionSystemOptions
        {
            WebhookAuthorizationLevel = level
        };

        Assert.Equal(level, options.WebhookAuthorizationLevel);
    }

    [Theory]
    [InlineData(AuthorizationLevel.Function)]
    [InlineData(AuthorizationLevel.Admin)]
    [InlineData(AuthorizationLevel.User)]
    [InlineData((AuthorizationLevel)999)]
    public void WebhookAuthorizationLevel_Set_InvalidValues_ThrowsArgumentOutOfRangeException(AuthorizationLevel level)
    {
        var options = new ExtensionSystemOptions();

        var ex = Assert.Throws<ArgumentOutOfRangeException>(() => options.WebhookAuthorizationLevel = level);

        Assert.Contains("Invalid AuthorizationLevel", ex.Message, StringComparison.Ordinal);
    }
}