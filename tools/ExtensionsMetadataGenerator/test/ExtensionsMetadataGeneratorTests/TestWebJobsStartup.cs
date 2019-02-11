﻿// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using ExtensionsMetadataGeneratorTests;
using Microsoft.Azure.WebJobs;
using Microsoft.Azure.WebJobs.Hosting;

[assembly: WebJobsStartup(typeof(FooWebJobsStartup))]
[assembly: WebJobsStartup(typeof(BarWebJobsStartup), "BarExtension")]

namespace ExtensionsMetadataGeneratorTests
{
    public class FooWebJobsStartup : IWebJobsStartup
    {
        public void Configure(IWebJobsBuilder builder)
        {
        }
    }

    public class BarWebJobsStartup : IWebJobsStartup
    {
        public void Configure(IWebJobsBuilder builder)
        {
        }
    }
}
