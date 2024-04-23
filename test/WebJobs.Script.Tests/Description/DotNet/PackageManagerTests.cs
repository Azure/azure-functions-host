// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System.IO;
using Microsoft.Azure.WebJobs.Script.Config;
using Microsoft.Azure.WebJobs.Script.Description;
using Xunit;

namespace Microsoft.Azure.WebJobs.Script.Tests
{
    public class PackageManagerTests
    {
        [Theory]
        [InlineData(@"ProjectWithLockMatch", false)]
        [InlineData(@"FunctionWithNoProject", false)]
        [InlineData(@"ProjectWithMismatchedLock/MismatchedPackageVersions", true)]
        [InlineData(@"ProjectWithMismatchedLock/MismatchedProjectDependencies", true)]
        [InlineData(@"ProjectWithoutLock", true)]
        public void RequirePackageRestore_ReturnsExpectedResult(string projectPath, bool shouldRequireRestore)
        {
            projectPath = Path.Combine(Directory.GetCurrentDirectory(), "Description", "DotNet", "TestFiles", "PackageReferences", projectPath);
            try
            {
                CopyLockFile(projectPath);
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
            finally
            {
                DeleteLockFile(projectPath);
            }
        }

        private static void CopyLockFile(string projectPath)
        {
            // We save the lock file as something other than 'project.assets.json' so it is not flagged by component governance.
            // Only renaming to project.assets.json for the duration of the test.
            string lockFile = Path.Combine(projectPath, DotNetConstants.ProjectLockFileName);
            string sourceFile = Path.Combine(projectPath, "test.assets.json");

            if (File.Exists(sourceFile))
            {
                File.Copy(sourceFile, lockFile, true);
            }
        }

        private static void DeleteLockFile(string projectPath)
        {
            string lockFile = Path.Combine(projectPath, DotNetConstants.ProjectLockFileName);
            if (File.Exists(lockFile))
            {
                File.Delete(lockFile);
            }
        }
    }
}
