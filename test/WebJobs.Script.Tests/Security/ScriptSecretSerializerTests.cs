// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System;
using Microsoft.Azure.WebJobs.Script.Config;
using Microsoft.Azure.WebJobs.Script.WebHost;
using Xunit;

namespace Microsoft.Azure.WebJobs.Script.Tests.Security
{
    public class ScriptSecretSerializerTests
    {
        private ScriptSettingsManager _settingsManager = ScriptSettingsManager.Instance;

        [Fact]
        public void DefaultSerializer_WhenMultiKeyFeatureIsNotSet_ReturnsV1Serializer()
        {
            Assert.Equal(typeof(ScriptSecretSerializerV1), ScriptSecretSerializer.DefaultSerializer?.GetType());
        }
    }
}
