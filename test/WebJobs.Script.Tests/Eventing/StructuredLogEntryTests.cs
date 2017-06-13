// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Microsoft.Azure.WebJobs.Script.Eventing;
using Newtonsoft.Json.Linq;
using Xunit;

namespace Microsoft.Azure.WebJobs.Script.Tests.Eventing
{
    public class StructuredLogEntryTests
    {
        [Fact]
        public void MultiLineValues_ReturnsSingleLine()
        {
            string entryName = "testlog";
            var logEntry = new StructuredLogEntry(entryName);

            logEntry.AddProperty("test", "multi\r\nline\r\nproperty");
            logEntry.AddProperty("jobject", new JObject { { "prop1", "val1" }, { "prop2", "val2" } });

            string result = logEntry.ToJsonLineString();

            Assert.DoesNotContain("\n", result);
        }
    }
}
