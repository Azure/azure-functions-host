// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System;
using System.IO;
using System.Linq;
using Microsoft.Extensions.Configuration;

namespace Microsoft.Azure.WebJobs.Script.Tests
{
    public static class TestConfigurationBuilderExtensions
    {
        private const string ConfigFile = "appsettings.tests.json";
        private static string configPath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".azurefunctions", ConfigFile);

        public static IConfigurationBuilder AddTestSettings(this IConfigurationBuilder builder) => builder.AddJsonFile(configPath, true);
    }
}
