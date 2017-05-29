// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System;
using Xunit;

namespace Microsoft.Azure.WebJobs.Script.Tests.Diagnostics
{
    public class FunctionTraceWriterFactoryTests
    {
        [Fact]
        public void Factory_CachesTraceWriters_CaseInsensitive()
        {
            var config = new ScriptHostConfiguration
            {
                FileLoggingMode = FileLoggingMode.Always
            };

            var factory = new FunctionTraceWriterFactory(config);

            var writer1 = factory.Create("TestFunction1");
            var writer2 = factory.Create("TestFunction1");

            var writer3 = factory.Create("TestFunction2");
            var writer4 = factory.Create("testfunction2");

            Assert.Same(writer1, writer2);
            Assert.Same(writer3, writer4);
            Assert.NotSame(writer1, writer3);
        }

        [Fact]
        public void Factory_Dispose_RemovesTraceWriter()
        {
            var config = new ScriptHostConfiguration
            {
                FileLoggingMode = FileLoggingMode.Always
            };

            var factory = new FunctionTraceWriterFactory(config);

            var writer1 = factory.Create("TestFunction1");
            var writer2 = factory.Create("TestFunction2");

            // This will remove the writer from the parent factory's cache.
            (writer1 as IDisposable).Dispose();

            var writer3 = factory.Create("TestFunction1");
            var writer4 = factory.Create("TestFunction2");

            Assert.NotSame(writer1, writer3);
            Assert.Same(writer2, writer4);
        }
    }
}
