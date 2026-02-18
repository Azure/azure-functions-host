// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.Azure.WebJobs.Script.Binding;
using Microsoft.Azure.WebJobs.Script.Configuration;
using Microsoft.Azure.WebJobs.Script.Extensibility;
using Microsoft.Extensions.Logging;
using Microsoft.WebJobs.Script.Tests;
using Newtonsoft.Json.Linq;
using Xunit;

namespace Microsoft.Azure.WebJobs.Script.Tests
{
    public class CoreExtensionsScriptBindingProviderTests
    {
        private readonly TestLoggerProvider _loggerProvider = new TestLoggerProvider();
        private readonly ILogger _logger;

        public CoreExtensionsScriptBindingProviderTests()
        {
            var loggerFactory = new LoggerFactory();
            loggerFactory.AddProvider(_loggerProvider);
            _logger = loggerFactory.CreateLogger<CoreExtensionsScriptBindingProvider>();
        }

        [Fact]
        public void GetAttributes_Error_ValidatesScheduleExpression()
        {
            var vars = new Dictionary<string, string>
            {
                { "TEST_SCHEDULE_CRON_6", "0 * * * * *" },
                { "TEST_SCHEDULE_CRON_5", "*/5 * * * *" },
                { "TEST_SCHEDULE_TIMESPAN", "00:00:15" },
            };

            var platformOptions = new TimerTriggerPlatformOptions
            {
                NonCronScheduleBehavior = NonCronScheduleBehavior.Error
            };

            var nameResolver = new TestNameResolver(vars);

            var triggerMetadata = new JObject
                {
                    { "direction", "in" },
                    { "name", "timer" },
                    { "type", "timerTrigger" }
                };
            var bindingContext = new ScriptBindingContext(triggerMetadata);
            var binding = new CoreExtensionsScriptBindingProvider.TimerTriggerScriptBinding(nameResolver, platformOptions, _logger, bindingContext);

            // TimeSpan expression is invalid and logs diagnostic error before throwing
            triggerMetadata["schedule"] = "00:00:15";
            var ex = Assert.Throws<ArgumentException>(() => binding.GetAttributes());
            Assert.Equal("'00:00:15' is not a valid CRON expression.", ex.Message);
            Assert.Single(_loggerProvider.GetAllLogMessages().Where(m => m.Level == LogLevel.Error));
            _loggerProvider.ClearAllLogMessages();

            // TimeSpan specified via app setting is invalid
            triggerMetadata["schedule"] = "%TEST_SCHEDULE_TIMESPAN%";
            ex = Assert.Throws<ArgumentException>(() => binding.GetAttributes());
            Assert.Equal("'00:00:15' is not a valid CRON expression.", ex.Message);
            Assert.Single(_loggerProvider.GetAllLogMessages().Where(m => m.Level == LogLevel.Error));
            _loggerProvider.ClearAllLogMessages();

            //// 6-digit Cron expression is valid
            triggerMetadata["schedule"] = "0 * * * * *";
            var timerAttribute = (TimerTriggerAttribute)binding.GetAttributes().Single();
            Assert.Equal("0 * * * * *", timerAttribute.ScheduleExpression);

            //// 6-digit Cron expression specified via app setting is valid
            triggerMetadata["schedule"] = "%TEST_SCHEDULE_CRON_6%";
            timerAttribute = (TimerTriggerAttribute)binding.GetAttributes().Single();
            Assert.Equal("0 * * * * *", timerAttribute.ScheduleExpression);

            //// 5-digit Cron expression is valid
            triggerMetadata["schedule"] = "*/5 * * * *";
            timerAttribute = (TimerTriggerAttribute)binding.GetAttributes().Single();
            Assert.Equal("*/5 * * * *", timerAttribute.ScheduleExpression);

            //// 5-digit Cron expression specified via app setting is valid
            triggerMetadata["schedule"] = "%TEST_SCHEDULE_CRON_5%";
            timerAttribute = (TimerTriggerAttribute)binding.GetAttributes().Single();
            Assert.Equal("*/5 * * * *", timerAttribute.ScheduleExpression);
        }

        [Fact]
        public void GetAttributes_Allow_SkipsValidation()
        {
            var platformOptions = new TimerTriggerPlatformOptions
            {
                NonCronScheduleBehavior = NonCronScheduleBehavior.Allow
            };

            var nameResolver = new TestNameResolver();

            var triggerMetadata = new JObject
                {
                    { "direction", "in" },
                    { "name", "timer" },
                    { "type", "timerTrigger" },
                    { "schedule", "00:00:15" }
                };
            var bindingContext = new ScriptBindingContext(triggerMetadata);
            var binding = new CoreExtensionsScriptBindingProvider.TimerTriggerScriptBinding(nameResolver, platformOptions, _logger, bindingContext);

            // TimeSpan expression passes when behavior is Allow
            var timerAttribute = (TimerTriggerAttribute)binding.GetAttributes().Single();
            Assert.Equal("00:00:15", timerAttribute.ScheduleExpression);
        }

        [Fact]
        public void GetAttributes_Warn_LogsDiagnosticWarning()
        {
            var platformOptions = new TimerTriggerPlatformOptions
            {
                NonCronScheduleBehavior = NonCronScheduleBehavior.Warn
            };

            var nameResolver = new TestNameResolver();

            var triggerMetadata = new JObject
                {
                    { "direction", "in" },
                    { "name", "timer" },
                    { "type", "timerTrigger" },
                    { "schedule", "00:00:15" }
                };
            var bindingContext = new ScriptBindingContext(triggerMetadata);
            var binding = new CoreExtensionsScriptBindingProvider.TimerTriggerScriptBinding(nameResolver, platformOptions, _logger, bindingContext);

            // TimeSpan expression is allowed but should log a diagnostic warning
            var timerAttribute = (TimerTriggerAttribute)binding.GetAttributes().Single();
            Assert.Equal("00:00:15", timerAttribute.ScheduleExpression);

            var warningLog = _loggerProvider.GetAllLogMessages().Single(m => m.Level == LogLevel.Warning);
            Assert.Contains("'00:00:15' is not a CRON expression", warningLog.FormattedMessage);
            Assert.Contains(DiagnosticEventConstants.TimerConstantExpressionWarningHelpLink, warningLog.FormattedMessage);
        }

        [Fact]
        public void GetAttributes_Warn_CronDoesNotWarn()
        {
            var platformOptions = new TimerTriggerPlatformOptions
            {
                NonCronScheduleBehavior = NonCronScheduleBehavior.Warn
            };

            var nameResolver = new TestNameResolver();

            var triggerMetadata = new JObject
                {
                    { "direction", "in" },
                    { "name", "timer" },
                    { "type", "timerTrigger" },
                    { "schedule", "0 * * * * *" }
                };
            var bindingContext = new ScriptBindingContext(triggerMetadata);
            var binding = new CoreExtensionsScriptBindingProvider.TimerTriggerScriptBinding(nameResolver, platformOptions, _logger, bindingContext);

            var timerAttribute = (TimerTriggerAttribute)binding.GetAttributes().Single();
            Assert.Equal("0 * * * * *", timerAttribute.ScheduleExpression);

            Assert.Empty(_loggerProvider.GetAllLogMessages().Where(m => m.Level == LogLevel.Warning));
        }
    }
}
