// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System;
using System.IO;
using Microsoft.Extensions.Configuration;
using Newtonsoft.Json.Linq;

namespace Microsoft.Azure.WebJobs.Script.Tests
{
    public static class TestConfigurationBuilderExtensions
    {
        private const string ConfigFile = "appsettings.tests.json";
        private static string configPath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".azurefunctions", ConfigFile);

        public static IConfigurationBuilder AddTestSettings(this IConfigurationBuilder builder) => builder.AddJsonFile(configPath, true);

        public static IConfigurationBuilder AddTestSettings(this IConfigurationBuilder builder, bool setStorageEnvironmentVariable)
        {
            if (setStorageEnvironmentVariable)
            {
                JObject config = JObject.Parse(File.ReadAllText(configPath));
                var storageConnection = config["AzureWebJobsStorage"].ToString();
                Environment.SetEnvironmentVariable("AzureWebJobsStorage", storageConnection);
            }

            return builder.AddTestSettings();
        }
    }
}
