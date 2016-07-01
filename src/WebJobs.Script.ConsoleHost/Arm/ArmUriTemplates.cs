using ARMClient.Library;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Web;

namespace WebJobs.Script.ConsoleHost.Arm
{
    public class ArmUriTemplates
    {
        private const string armApiVersion = "2014-04-01";
        private const string websitesApiVersion = "2015-08-01";
        private const string storageApiVersion = "2015-05-01-preview";
        private const string ArmUrl = "https://management.azure.com";

        public static readonly ArmUriTemplate Subscriptions = new ArmUriTemplate($"{ArmUrl}/subscriptions", armApiVersion);
        public static readonly ArmUriTemplate Subscription = new ArmUriTemplate($"{Subscriptions.TemplateUrl}/{{subscriptionId}}", armApiVersion);
        public static readonly ArmUriTemplate SubscriptionResources = new ArmUriTemplate(Subscription.TemplateUrl + "/resources", armApiVersion);
        public static readonly ArmUriTemplate SubscriptionWebApps = new ArmUriTemplate(Subscription.TemplateUrl + "/resources?$filter=resourceType eq 'Microsoft.Web/sites'", armApiVersion);

        public static readonly ArmUriTemplate ResourceGroups = new ArmUriTemplate($"{Subscription.TemplateUrl}/resourceGroups", armApiVersion);
        public static readonly ArmUriTemplate ResourceGroup = new ArmUriTemplate($"{ResourceGroups.TemplateUrl}/{{resourceGroupName}}", armApiVersion);
        public static readonly ArmUriTemplate ResourceGroupResources = new ArmUriTemplate($"{ResourceGroup.TemplateUrl}/resources", armApiVersion);

        public static readonly ArmUriTemplate WebsitesRegister = new ArmUriTemplate($"{Subscription.TemplateUrl}/providers/Microsoft.Web/register", websitesApiVersion);

        public static readonly ArmUriTemplate Sites = new ArmUriTemplate($"{ResourceGroup.TemplateUrl}/providers/Microsoft.Web/sites", websitesApiVersion);
        public static readonly ArmUriTemplate Site = new ArmUriTemplate($"{Sites.TemplateUrl}/{{siteName}}", websitesApiVersion);
        public static readonly ArmUriTemplate SiteRestart = new ArmUriTemplate($"{Sites.TemplateUrl}/{{siteName}}/restart", websitesApiVersion);
        public static readonly ArmUriTemplate SitePublishingCredentials = new ArmUriTemplate($"{Site.TemplateUrl}/config/PublishingCredentials/list", websitesApiVersion);
        public static readonly ArmUriTemplate ListSiteAppSettings = new ArmUriTemplate($"{Site.TemplateUrl}/config/appsettings/list", websitesApiVersion);
        public static readonly ArmUriTemplate PutSiteAppSettings = new ArmUriTemplate($"{Site.TemplateUrl}/config/appsettings", websitesApiVersion);
        public static readonly ArmUriTemplate SubscriptionLevelServerFarms = new ArmUriTemplate($"{Subscription.TemplateUrl}/providers/Microsoft.Web/serverfarms", websitesApiVersion);
        public static readonly ArmUriTemplate SiteConfig = new ArmUriTemplate($"{Site.TemplateUrl}/config/web", websitesApiVersion);

        public static readonly ArmUriTemplate StorageRegister = new ArmUriTemplate($"{Subscription.TemplateUrl}/providers/Microsoft.Storage/register", storageApiVersion);
        public static readonly ArmUriTemplate StorageAccounts = new ArmUriTemplate($"{ResourceGroup.TemplateUrl}/providers/Microsoft.Storage/storageAccounts", storageApiVersion);
        public static readonly ArmUriTemplate StorageAccount = new ArmUriTemplate($"{StorageAccounts.TemplateUrl}/{{storageAccountName}}", storageApiVersion);
        public static readonly ArmUriTemplate StorageListKeys = new ArmUriTemplate($"{StorageAccount.TemplateUrl}/listKeys", storageApiVersion);

    }
}