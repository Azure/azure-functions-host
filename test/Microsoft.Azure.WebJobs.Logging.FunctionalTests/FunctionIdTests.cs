// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using Xunit;

namespace Microsoft.Azure.WebJobs.Logging.FunctionalTests
{
    public class FunctionIdTests
    {
        // Test normalization 
        [Fact]
        public void Operators()
        {
            var f1 = FunctionId.Build("h1", "FFF1");
            var f2 = FunctionId.Build("H1", "fff1"); // different casing. 

            Assert.Equal("h1-fff1", f2.ToString());

            // Names have been normalized.
            Assert.True(f1.Equals(f2));
            Assert.Equal(f1.GetHashCode(), f2.GetHashCode());
            Assert.True(f1 == f2);
            Assert.False(f1 != f2);
        }

        // Characters are escaped. 
        [Fact]
        public void Escapes()
        {
            var f = FunctionId.Build("h-1", "F-:1");

            string str = f.ToString();
            Assert.Equal("h:2D1-f:2D:3A1", str);
        }

        // Direct creation doesn't roundtrip. 
        [Fact]
        public void Direct()
        {
            string str = "ab-def%:x$";
            var f = FunctionId.Parse(str);
                        
            Assert.Equal(str, f.ToString());
        }
    }
}
