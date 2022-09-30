﻿// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System;
using System.Threading.Tasks;
using Microsoft.Azure.WebJobs.Host.Scale;

namespace Microsoft.Azure.WebJobs.Script.Tests
{
    internal class TestTargetScaler : ITargetScaler
    {
        public TargetScalerDescriptor TargetScalerDescriptor { get; set; }

        public Task<TargetScalerResult> GetScaleResultAsync(TargetScalerContext context)
        {
            throw new NotImplementedException();
        }
    }

    internal class TestTargetScaler1 : TestTargetScaler
    {
    }
}
