// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System;
using System.Configuration;
using System.Text.RegularExpressions;

namespace WebJobs.Script.EndToEndTests
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

        public static Uri SiteBaseAddress => new Uri($"https://{SiteName}.azurewebsites.net");

        public static string RuntimeVersion
        {
            get
            {
                Match versionMatch = Regex.Match(RuntimeExtensionPackageUrl, "(?<version>\\d*\\.\\d*\\.\\d*)(-.*)?\\.zip$");

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
            string value = Environment.GetEnvironmentVariable(settingName);

            if (string.IsNullOrEmpty(value))
            {
                value = ConfigurationManager.AppSettings.Get(settingName);
            }

            return value;
        }
    }
}
