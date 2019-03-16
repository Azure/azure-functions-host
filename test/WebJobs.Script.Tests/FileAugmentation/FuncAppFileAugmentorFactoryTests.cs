// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System;
using System.Collections.Generic;
using System.Text;
using Microsoft.Azure.WebJobs.Script.FileAugmentation;
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
        public void CreatFileAugmentor_Null_Or_Empty_Runtime(string runtime)
        {
            var fileAugmentor = _funcAppFileAugmentorFactory.CreatFileAugmentor(runtime);
            Assert.True(fileAugmentor == null);
        }
    }
}
