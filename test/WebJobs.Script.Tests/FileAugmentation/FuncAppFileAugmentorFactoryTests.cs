// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System;
using Microsoft.Azure.WebJobs.Script.FileAugmentation;
using Microsoft.Azure.WebJobs.Script.FileAugmentation.PowerShell;
using Xunit;

namespace Microsoft.Azure.WebJobs.Script.Tests.FileAugmentation
{
    public class FuncAppFileAugmentorFactoryTests
    {
        private readonly IFuncAppFileAugmentorFactory _funcAppFileAugmentorFactory;

        public FuncAppFileAugmentorFactoryTests()
        {
            _funcAppFileAugmentorFactory = new FuncAppFileAugmentorFactory();
        }

        [Theory]
        [InlineData("")]
        [InlineData(null)]
        [InlineData("dotnet")]
        [InlineData("powershell")]
        public void CreatFileAugmentor_Test(string runtime)
        {
            var fileAugmentor = _funcAppFileAugmentorFactory.CreatFileAugmentor(runtime);
            if (string.Equals(runtime, "powershell", StringComparison.InvariantCultureIgnoreCase))
            {
                Assert.True(fileAugmentor != null);
                Assert.True(fileAugmentor is PowerShellFileAugmentor);
            }
            else
            {
                Assert.True(fileAugmentor == null);
            }
        }
    }
}
