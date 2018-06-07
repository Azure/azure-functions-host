// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using Microsoft.Azure.WebJobs.Script.Description;
using Microsoft.Extensions.DependencyModel;
using Xunit;

namespace Microsoft.Azure.WebJobs.Script.Tests.Description
{
    public class DependencyHelperTests
    {
        [Fact]
        public void GetDefaultRuntimeFallbacks_MatchesCurrentRuntimeFallbacks()
        {
            foreach (var fallback in DependencyContext.Default.RuntimeGraph)
            {
                RuntimeFallbacks result = DependencyHelper.GetDefaultRuntimeFallbacks(fallback.Runtime);

                bool match = result.Fallbacks
                    .Zip(fallback.Fallbacks, (s1, s2) => string.Equals(s1, s2, StringComparison.Ordinal))
                    .All(r => r);

                Assert.True(match, $"Mismatched fallbacks for RID '{fallback.Runtime}'");
            }
        }
    }
}
