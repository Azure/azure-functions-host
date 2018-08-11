// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.Azure.WebJobs.Script.Binding;
using Microsoft.Azure.WebJobs.Script.Extensibility;
using Moq;
using Newtonsoft.Json.Linq;
using Xunit;

namespace Microsoft.Azure.WebJobs.Script.Tests
{
    public class CoreExtensionsScriptBindingProviderTests
    {
        [Fact]
        public void GetAttributes_DynamicSku_ValidatesScheduleExpression()
        {
            var vars = new Dictionary<string, string>
            {
                { "TEST_SCHEDULE_CRON", "0 * * * * *" },
                { "TEST_SCHEDULE_TIMESPAN", "00:00:15" },
            };

            var environment = new TestEnvironment();
            environment.SetEnvironmentVariable("WEBSITE_SKU", "Dynamic");

            var nameResolver = new TestNameResolver(vars);

            var triggerMetadata = new JObject
                {
                    { "direction", "in" },
                    { "name", "timer" },
                    { "type", "timerTrigger" }
                };
            var bindingContext = new ScriptBindingContext(triggerMetadata);
            var binding = new CoreExtensionsScriptBindingProvider.TimerTriggerScriptBinding(nameResolver, environment, bindingContext);

            // TimeSpan expression is invalid
            triggerMetadata["schedule"] = "00:00:15";
            var ex = Assert.Throws<ArgumentException>(() => binding.GetAttributes());
            Assert.Equal("'00:00:15' is not a valid CRON expression.", ex.Message);

            // TimeSpan specified via app setting is invalid
            triggerMetadata["schedule"] = "%TEST_SCHEDULE_TIMESPAN%";
            ex = Assert.Throws<ArgumentException>(() => binding.GetAttributes());
            Assert.Equal("'00:00:15' is not a valid CRON expression.", ex.Message);

            //// Cron expression is valid
            triggerMetadata["schedule"] = "0 * * * * *";
            var timerAttribute = (TimerTriggerAttribute)binding.GetAttributes().Single();
            Assert.Equal("0 * * * * *", timerAttribute.ScheduleExpression);

            //// Cron expression specified via app setting is valid
            triggerMetadata["schedule"] = "%TEST_SCHEDULE_CRON%";
            timerAttribute = (TimerTriggerAttribute)binding.GetAttributes().Single();
            Assert.Equal("0 * * * * *", timerAttribute.ScheduleExpression);
        }
    }
}
