// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using Microsoft.Extensions.Configuration;
using System;
using System.Configuration;
using System.IO;
using System.Text.RegularExpressions;

namespace WebJobs.Script.Tests.EndToEnd.Shared
{
    public class Settings
    {
        public static string SiteTenantId => GetSettingValue(Constants.TargetSiteTenantIdSettingName);

        public static string SiteClientSecret => GetSettingValue(Constants.TargetSiteClientSecretSettingName);

        public static string SiteApplicationId => GetSettingValue(Constants.TargetSiteApplicationIdSettingName);

        public static string SiteSubscriptionId => GetSettingValue(Constants.TargetSiteSubscriptionIdSettingName);

        public static string SiteResourceGroup => GetSettingValue(Constants.TargetSiteResourceGroupSettingName);

        public static string SiteName => GetSettingValue(Constants.TargetSiteNameSettingName);

        public static string SitePublishingUser => GetSettingValue(Constants.TargetSitePublishingUserSettingName);

        public static string SitePublishingPassword => GetSettingValue(Constants.TargetSitePublishingPasswordSettingName);

        public static string RuntimeExtensionPackageUrl => GetSettingValue(Constants.RuntimeExtensionPackageUrlSettingName);

        public static string SiteMasterKey => GetSettingValue(Constants.TargetSiteMasterKey);

        public static string SiteFunctionKey => GetSettingValue(Constants.TargetSiteFunctionKey);

        public static string VM => GetSettingValue(Constants.VM);

        public static Uri SiteBaseAddress => new Uri($"https://{SiteName}.azurewebsites.net");

        private static IConfiguration Config = null;

        public static string RuntimeVersion
        {
            get
            {
                Match versionMatch = Regex.Match(RuntimeExtensionPackageUrl, "(\\.)(?<version>\\d*\\.\\d*\\.\\d*)(\\-prerelease.*|\\..*)?\\.zip$");

                if (!versionMatch.Success)
                {
                    throw new Exception("Unable to resolve the Function runtime version from package URL");
                }

                // Adding a revision number here as it is returned by the status endpoint
                // Eventually we may want to consider updating the extension packages with that information.
                return versionMatch.Groups["version"].Value + ".0";
            }
        }

        private static string GetSettingValue(string settingName)
        {
            if (Config == null)
            {
                var builder = new ConfigurationBuilder().AddEnvironmentVariables();
                if (File.Exists("local.settings.json"))
                {
                    builder.AddJsonFile("local.settings.json");
                }
                Config = builder.Build();
            }


            return ConfigurationBinder.GetValue(Config, settingName, "default");
        }
    }
}
