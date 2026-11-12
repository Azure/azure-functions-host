// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System;
using Microsoft.Azure.WebJobs.Script.Description;
using Xunit;

namespace Microsoft.Azure.WebJobs.Script.Tests;

public class InProcessFunctionValidatorTests
{
    [Theory]
    [InlineData("dotnet")]
    [InlineData("DOTNET")]
    public void ValidateRuntime_RejectsRetiredRuntime(string runtime)
    {
        var exception = Assert.Throws<HostInitializationException>(() =>
            InProcessFunctionValidator.ValidateRuntime(runtime, isPlaceholder: false));

        Assert.Contains("Migrate", exception.Message);
        InProcessFunctionValidator.ValidateRuntime(runtime, isPlaceholder: true);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("dotnet-isolated")]
    [InlineData("node")]
    [InlineData("custom")]
    public void ValidateRuntime_PreservesWorkerAndEmptyRuntime(string runtime)
    {
        InProcessFunctionValidator.ValidateRuntime(runtime, isPlaceholder: false);
    }

    [Theory]
    [InlineData("dotnet")]
    [InlineData("DOTNET")]
    public void ValidateRuntime_PreservesMultiLanguageRuntime(string runtime)
    {
        InProcessFunctionValidator.ValidateRuntime(runtime, isPlaceholder: false, isMultiLanguage: true);
    }

    [Theory]
    [InlineData("CSharp", false)]
    [InlineData("DotNetAssembly", false)]
    [InlineData("RawDotNetAssembly", false)]
    [InlineData("dotnet-isolated", true)]
    public void ValidateMetadata_RejectsCustomerMetadataRegardlessOfCodelessFlag(string language, bool direct)
    {
        var metadata = new FunctionMetadata { Name = "Retired", Language = language };
        metadata.SetIsDirect(direct);
        metadata.SetIsCodeless(true);
        metadata.SetIsDisabled(true);

        var exception = Assert.Throws<HostInitializationException>(() => InProcessFunctionValidator.ValidateMetadata(metadata));

        Assert.Contains(metadata.Name, exception.Message);
        Assert.Contains(InProcessFunctionValidator.ErrorMessage, exception.Message);
    }

    [Fact]
    public void ValidateMetadata_PreservesProviderAssemblyButRejectsProviderScripts()
    {
        var metadata = new FunctionMetadata { Name = "Extension", Language = DotNetScriptTypes.DotNetAssembly };
        metadata.SetIsCodeless(true);
        metadata.SetIsExtensionFunction(true);

        InProcessFunctionValidator.ValidateMetadata(metadata);

        metadata.Language = DotNetScriptTypes.CSharp;
        Assert.Throws<HostInitializationException>(() => InProcessFunctionValidator.ValidateMetadata(metadata));
    }

    [Fact]
    public void ValidateMetadata_DoesNotClassifyWorkerDllByExtension()
    {
        InProcessFunctionValidator.ValidateMetadata(new FunctionMetadata
        {
            Name = "WorkerFunction",
            Language = "dotnet-isolated",
            ScriptFile = "function.dll"
        });
    }
}
