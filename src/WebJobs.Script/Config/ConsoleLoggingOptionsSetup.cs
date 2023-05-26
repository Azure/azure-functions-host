// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Options;

namespace Microsoft.Azure.WebJobs.Script.Config
{
    public class ConsoleLoggingOptionsSetup : IConfigureOptions<ConsoleLoggingOptions>
    {
        private readonly IConfiguration _configuration;

        public ConsoleLoggingOptionsSetup(IConfiguration configuration)
        {
            _configuration = configuration;
        }

        public void Configure(ConsoleLoggingOptions options)
        {
            options.LoggingDisabled = _configuration.GetValue<int>(EnvironmentSettingNames.ConsoleLoggingDisabled) == 1;

            int? bufferSize = _configuration.GetValue<int?>(EnvironmentSettingNames.ConsoleLoggingBufferSize);
            if (bufferSize != null)
            {
                if (bufferSize < 0)
                {
                    throw new ArgumentOutOfRangeException(nameof(EnvironmentSettingNames.ConsoleLoggingBufferSize), "Console buffer size cannot be negative");
                }

                options.BufferEnabled = bufferSize > 0;
                options.BufferSize = bufferSize.Value;
            }
        }
    }
}
