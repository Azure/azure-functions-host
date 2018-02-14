// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using Microsoft.Azure.WebJobs.Logging;
using Microsoft.Azure.WebJobs.Script.Config;
using Microsoft.Azure.WebJobs.Script.WebHost.Diagnostics;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace Microsoft.Azure.WebJobs.Script.Tests
{
    public class SystemLoggerProviderTests
    {
        [Fact]
        public void CreateLogger_ReturnsSystemLogger_ForNonUserCategories()
        {
            var provider = new SystemLoggerProvider(null, ScriptSettingsManager.Instance);

            Assert.IsType<SystemLogger>(provider.CreateLogger(LogCategories.CreateFunctionCategory("TestFunction")));
            Assert.IsType<SystemLogger>(provider.CreateLogger(ScriptConstants.LogCategoryHostGeneral));
            Assert.IsType<SystemLogger>(provider.CreateLogger("NotAFunction.TestFunction.User"));
        }

        [Fact]
        public void CreateLogger_ReturnsNullLogger_ForUserCategory()
        {
            var provider = new SystemLoggerProvider(null, ScriptSettingsManager.Instance);

            Assert.IsType<NullLogger>(provider.CreateLogger(LogCategories.CreateFunctionUserCategory("TestFunction")));
        }
    }
}