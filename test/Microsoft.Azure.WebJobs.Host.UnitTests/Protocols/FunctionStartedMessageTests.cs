// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System.Diagnostics;
using Microsoft.Azure.WebJobs.Host.Protocols;
using Newtonsoft.Json;
using Xunit;

namespace Microsoft.Azure.WebJobs.Host.UnitTests.Protocols
{
    public class FunctionStartedMessageTests
    {
        [Fact]
        public void LogLevel_IsStringEnumSerialized()
        {
            FunctionStartedMessage message = new FunctionStartedMessage()
            {
                LogLevel = TraceLevel.Verbose
            };
            string json = JsonConvert.SerializeObject(message);
            Assert.True(json.Contains("\"LogLevel\":\"Verbose\""));

            FunctionStartedMessage result = JsonConvert.DeserializeObject<FunctionStartedMessage>(json);
            Assert.Equal(TraceLevel.Verbose, result.LogLevel);
        }
    }
}
