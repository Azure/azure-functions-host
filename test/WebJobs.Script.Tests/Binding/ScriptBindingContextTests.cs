// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System.IO;
using Microsoft.Azure.WebJobs.Script.Extensibility;
using Newtonsoft.Json.Linq;
using Xunit;

namespace Microsoft.Azure.WebJobs.Script.Tests.Binding
{
    public class ScriptBindingContextTests
    {
        [Fact]
        public void Constructor_InitializesDefaultProperties()
        {
            JObject metadata = new JObject
            {
                { "name", "TestFunc" },
                { "direction", "inout" },
                { "type", "queueTrigger" },
                { "dataType", "string" }
            };

            ScriptBindingContext context = new ScriptBindingContext(metadata);

            Assert.Same(metadata, context.Metadata);
            Assert.Equal(context.Name, "TestFunc");
            Assert.Equal(context.Access, FileAccess.ReadWrite);
            Assert.Equal(context.Type, "queueTrigger");
            Assert.Equal(context.DataType, "string");
        }
    }
}
