// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System;
using System.IO;
using Microsoft.Extensions.Configuration;

namespace Microsoft.Azure.WebJobs.Script.Tests
{
    public static class TestConfigurationBuilderExtensions
    {
        public static IConfigurationBuilder AddTestSettings(this IConfigurationBuilder builder)
        {
            string configPath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".azurefunctions", "appsettings.tests.json");
            return builder.AddJsonFile(configPath, true);
        }
    }
}
