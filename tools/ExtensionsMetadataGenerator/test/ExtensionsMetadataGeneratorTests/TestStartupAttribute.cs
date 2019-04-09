// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System;
using System.Collections.Generic;
using System.Text;
using Microsoft.Azure.WebJobs.Hosting;

namespace ExtensionsMetadataGeneratorTests
{
    public class TestStartupAttribute : WebJobsStartupAttribute
    {
        public TestStartupAttribute(Type startupType, string name = null) : base(startupType, name)
        {
        }
    }
}
