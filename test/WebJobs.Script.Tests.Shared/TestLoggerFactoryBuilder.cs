// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using Microsoft.Azure.WebJobs.Script.Config;
using Microsoft.Extensions.Logging;

namespace Microsoft.Azure.WebJobs.Script.Tests
{
    public class TestLoggerFactoryBuilder : ILoggerFactoryBuilder
    {
        private TestLoggerProvider _provider;

        public TestLoggerFactoryBuilder(TestLoggerProvider provider)
        {
            _provider = provider;
        }

        public void AddLoggerProviders(ILoggerFactory factory, ScriptHostConfiguration scriptConfig, ScriptSettingsManager settingsManager)
        {
            factory.AddProvider(_provider);
        }
    }
}
