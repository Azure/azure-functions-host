// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Microsoft.Azure.WebJobs.Script.Description;
using Xunit;

namespace Microsoft.Azure.WebJobs.Script.Tests.Description.DotNet
{
    public class PackageManagerTests
    {
        [Theory]
        [InlineData(@"ProjectWithLockMatch", false)]
        [InlineData(@"FunctionWithNoProject", false)]
        [InlineData(@"ProjectWithMismatchedLock\MismatchedGroupFrameworkDependencies", true)]
        [InlineData(@"ProjectWithMismatchedLock\MismatchedPackageVersions", true)]
        [InlineData(@"ProjectWithMismatchedLock\MismatchedProjectDependencies", true)]
        [InlineData(@"ProjectWithoutLock", true)]
        public void RequirePackageRestore_ReturnsExpectedResult(string projectPath, bool shouldRequireRestore)
        {
            projectPath = Path.Combine(Directory.GetCurrentDirectory(), @"Description\DotNet\TestFiles\PackageReferences", projectPath);
            bool result = PackageManager.RequiresPackageRestore(projectPath);

            Assert.True(Directory.Exists(projectPath));

            string message = $"Project in '{projectPath}' did not return expected result.";
            // Using .True or .False (instead of .Equal) to trace additional information.
            if (shouldRequireRestore)
            {
                Assert.True(result, message);
            }
            else
            {
                Assert.False(result, message);
            }
        }
    }
}
