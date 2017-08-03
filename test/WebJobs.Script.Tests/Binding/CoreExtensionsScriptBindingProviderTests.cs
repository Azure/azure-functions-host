// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System;
using System.Linq;
using Microsoft.Azure.WebJobs.Extensions;
using Microsoft.Azure.WebJobs.Script.Binding;
using Microsoft.Azure.WebJobs.Script.Extensibility;
using Newtonsoft.Json.Linq;
using Xunit;

namespace Microsoft.Azure.WebJobs.Script.Tests
{
    public class CoreExtensionsScriptBindingProviderTests
    {
        [Fact]
        public void GetAttributes_DynamicSku_ValidatesScheduleExpression()
        {
            Environment.SetEnvironmentVariable("TEST_SCHEDULE_CRON", "0 * * * * *");
            Environment.SetEnvironmentVariable("TEST_SCHEDULE_TIMESPAN", "00:00:15");
            Environment.SetEnvironmentVariable("WEBSITE_SKU", "Dynamic");

            try
            {
                var triggerMetadata = new JObject
                {
                    { "direction", "in" },
                    { "name", "timer" },
                    { "type", "timerTrigger" }
                };
                var bindingContext = new ScriptBindingContext(triggerMetadata);
                var binding = new CoreExtensionsScriptBindingProvider.TimerTriggerScriptBinding(bindingContext);

                // TimeSpan expression is invalid
                triggerMetadata["schedule"] = "00:00:15";
                var ex = Assert.Throws<ArgumentException>(() => binding.GetAttributes());
                Assert.Equal("'00:00:15' is not a valid CRON expression. Schedule expressions in the form HH:MM:SS can only be used in an App Service Plan.", ex.Message);

                // TimeSpan specified via app setting is invalid
                triggerMetadata["schedule"] = "%TEST_SCHEDULE_TIMESPAN%";
                ex = Assert.Throws<ArgumentException>(() => binding.GetAttributes());
                Assert.Equal("'00:00:15' is not a valid CRON expression. Schedule expressions in the form HH:MM:SS can only be used in an App Service Plan.", ex.Message);

                // Cron expression is valid
                triggerMetadata["schedule"] = "0 * * * * *";
                var timerAttribute = (TimerTriggerAttribute)binding.GetAttributes().Single();
                Assert.Equal("0 * * * * *", timerAttribute.ScheduleExpression);

                // Cron expression specified via app setting is valid
                triggerMetadata["schedule"] = "%TEST_SCHEDULE_CRON%";
                timerAttribute = (TimerTriggerAttribute)binding.GetAttributes().Single();
                Assert.Equal("0 * * * * *", timerAttribute.ScheduleExpression);
            }
            finally
            {
                Environment.SetEnvironmentVariable("WEBSITE_SKU", null);
            }
        }
    }
}
