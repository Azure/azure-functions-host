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

        [Theory]
        [InlineData("win11-x86")]
        public void GetRuntimeFallbacks_WithUnkonwnRid_DefaultsToPlatformRid(string rid)
        {
            List<string> rids = DependencyHelper.GetRuntimeFallbacks(rid);

            // Ensure the "unknown" RID is still in the list
            Assert.Equal(rid, rids.First());

            // Ensure our fallback list matches our default RID fallback
            var defaultRidFallback = DependencyHelper.GetDefaultRuntimeFallbacks(DotNetConstants.DefaultWindowsRID);
            var defaultRidGraph = new List<string> { defaultRidFallback.Runtime };
            defaultRidGraph.AddRange(defaultRidFallback.Fallbacks);

            bool match = defaultRidGraph
                    .Zip(rids.Skip(1), (s1, s2) => string.Equals(s1, s2, StringComparison.Ordinal))
                    .All(r => r);

            Assert.True(match, $"Mismatched fallbacks for unknown RID");
        }

        [Theory]
        [InlineData(null, false)]
        [InlineData("", false)]
        [InlineData("assembly:myassembly", true)]
        [InlineData("Assembly:myassembly", false)]
        [InlineData("assembly", false)]
        [InlineData("assembly:", true)]
        public void IsSharedAssemblyFormat_Works(string formatStr, bool expected)
        {
            var result = DependencyHelper.IsAssemblyReferenceFormat(formatStr);
            Assert.Equal(expected, result);
        }

        [Theory]
        [InlineData(null, null)]
        [InlineData("", null)]
        [InlineData("assembly:myassembly", "myassembly")]
        [InlineData("Assembly:myassembly", null)]
        [InlineData("assembly", null)]
        [InlineData("assembly:", "")]
        public void TryGetSharedAssembly_Works(string formatStr, string expected)
        {
            DependencyHelper.TryGetAssemblyReference(formatStr, out string result);
            Assert.Equal(expected, result);
        }
    }
}
