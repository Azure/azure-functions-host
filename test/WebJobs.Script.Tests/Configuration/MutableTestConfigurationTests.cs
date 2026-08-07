// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System.Collections.Generic;
using System.Linq;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Primitives;
using Xunit;

namespace Microsoft.Azure.WebJobs.Script.Tests.Configuration;

public class MutableTestConfigurationTests
{
    [Fact]
    public void MutationIsObservedOnlyAfterExplicitReload()
    {
        MutableTestConfiguration configuration = new(
            new Dictionary<string, string> { ["Key"] = "initial" });
        IChangeToken reloadToken = configuration.Configuration.GetReloadToken();

        configuration.Set("key", "updated");

        Assert.Equal("initial", configuration.Configuration["KEY"]);
        Assert.False(reloadToken.HasChanged);

        configuration.Reload();

        Assert.Equal("updated", configuration.Configuration["KEY"]);
        Assert.True(reloadToken.HasChanged);
    }

    [Fact]
    public void RemovalRevealsLowerProviderAfterReload()
    {
        MutableTestConfiguration configuration = new(
            new Dictionary<string, string> { ["Key"] = "override" },
            builder => builder.AddInMemoryCollection(
                new Dictionary<string, string> { ["Key"] = "fallback" }));

        configuration.Remove("Key");
        configuration.Reload();

        Assert.Equal("fallback", configuration.Configuration["Key"]);
    }

    [Fact]
    public void NullAndEmptyValuesRemainDistinctConfigurationValues()
    {
        MutableTestConfiguration configuration = new();

        configuration.Set("Null", null);
        configuration.Set("Empty", string.Empty);
        configuration.Reload();

        Assert.Null(configuration.Configuration["Null"]);
        Assert.Equal(string.Empty, configuration.Configuration["Empty"]);
        Assert.True(configuration.Configuration.AsEnumerable().Any(
            pair => string.Equals(pair.Key, "Null", System.StringComparison.OrdinalIgnoreCase)));
    }
}
