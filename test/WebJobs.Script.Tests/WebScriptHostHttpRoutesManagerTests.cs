// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System;
using Microsoft.Azure.WebJobs.Script.WebHost;
using Xunit;

namespace Microsoft.Azure.WebJobs.Script.Tests;

public class WebScriptHostHttpRoutesManagerTests
{
    // BuildConstraintMethods

    [Theory]
    [InlineData(new[] { "get" }, new[] { "get", "head" })]
    [InlineData(new[] { "GET" }, new[] { "GET", "head" })]
    [InlineData(new[] { "get", "post" }, new[] { "get", "post", "head" })]
    [InlineData(new[] { "get", "head" }, new[] { "get", "head" })]  // no duplicate
    [InlineData(new[] { "GET", "HEAD" }, new[] { "GET", "HEAD" })]  // case-insensitive no duplicate
    [InlineData(new[] { "post" }, new[] { "post" })]                // no GET, unchanged
    [InlineData(new[] { "post", "put" }, new[] { "post", "put" })]  // no GET, unchanged
    public void BuildConstraintMethods_WithMethods_ReturnsExpected(string[] input, string[] expected)
    {
        string[] result = WebScriptHostHttpRoutesManager.BuildConstraintMethods(input);
        Assert.Equal(expected, result);
    }

    [Fact]
    public void BuildConstraintMethods_NullMethods_ReturnsNull()
    {
        string[] result = WebScriptHostHttpRoutesManager.BuildConstraintMethods(null);
        Assert.Null(result);
    }

    // ShouldRegisterHeadNotAllowedRoute

    [Theory]
    [InlineData(new[] { "post" }, true)]
    [InlineData(new[] { "post", "put" }, true)]
    [InlineData(new[] { "DELETE" }, true)]
    public void ShouldRegisterHeadNotAllowedRoute_NonGetNonHead_ReturnsTrue(string[] methods, bool _)
    {
        Assert.True(WebScriptHostHttpRoutesManager.ShouldRegisterHeadNotAllowedRoute(methods));
    }

    [Theory]
    [InlineData(new[] { "get" }, false)]
    [InlineData(new[] { "head" }, false)]
    [InlineData(new[] { "get", "post" }, false)]
    [InlineData(new[] { "GET", "POST" }, false)]
    public void ShouldRegisterHeadNotAllowedRoute_HasGetOrHead_ReturnsFalse(string[] methods, bool _)
    {
        Assert.False(WebScriptHostHttpRoutesManager.ShouldRegisterHeadNotAllowedRoute(methods));
    }

    [Fact]
    public void ShouldRegisterHeadNotAllowedRoute_NullMethods_ReturnsFalse()
    {
        Assert.False(WebScriptHostHttpRoutesManager.ShouldRegisterHeadNotAllowedRoute(null));
    }

    // BuildHeadNotAllowedSentinelName

    [Fact]
    public void BuildHeadNotAllowedSentinelName_SingleMethod_FormatsCorrectly()
    {
        string result = WebScriptHostHttpRoutesManager.BuildHeadNotAllowedSentinelName(["post"]);
        Assert.Equal("$head_not_allowed:POST", result);
    }

    [Fact]
    public void BuildHeadNotAllowedSentinelName_MultipleMethods_JoinsUppercase()
    {
        string result = WebScriptHostHttpRoutesManager.BuildHeadNotAllowedSentinelName(["post", "put"]);
        Assert.Equal("$head_not_allowed:POST, PUT", result);
    }

    [Fact]
    public void BuildHeadNotAllowedSentinelName_StartsWithExpectedPrefix()
    {
        string result = WebScriptHostHttpRoutesManager.BuildHeadNotAllowedSentinelName(["delete"]);
        Assert.StartsWith(ScriptConstants.HeadMethodNotAllowedPrefix, result, StringComparison.Ordinal);
    }
}
