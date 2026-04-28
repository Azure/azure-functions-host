// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using Xunit;

namespace Microsoft.Azure.Functions.WorkerProxy.Tests;

public class CapabilityLogFormatterTests
{
    [Fact]
    public void Format_ReturnsEmptyObject_WhenCapabilitiesAreMissing()
    {
        Assert.Equal("{}", CapabilityLogFormatter.Format(null));
        Assert.Equal("{}", CapabilityLogFormatter.Format(Array.Empty<KeyValuePair<string, string>>()));
    }

    [Fact]
    public void Format_OrdersKeysAndSummarizesHostConfigurationJson()
    {
        Dictionary<string, string> capabilities = new()
        {
            ["z-key"] = "last",
            ["host_configuration_json"] = """{"version":"2.0"}""",
            ["a-key"] = "first"
        };

        string result = CapabilityLogFormatter.Format(capabilities);

        Assert.Equal("""{"a-key":"first","host_configuration_json":"\u003Comitted; length=17\u003E","z-key":"last"}""", result);
    }
}
