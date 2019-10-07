using System;

namespace WebJobs.Script.Tests.Perf.Dashboard
{
    public class PerformanceRunOptions
    {
        public PerformanceRunOptions()
        {
            ClientId = Environment.GetEnvironmentVariable("AzureWebJobsTargetSiteApplicationId", EnvironmentVariableTarget.Process);
            ClientSecret = Environment.GetEnvironmentVariable("AzureWebJobsTargetSiteClientSecret", EnvironmentVariableTarget.Process);
            TenantId = Environment.GetEnvironmentVariable("AzureWebJobsTargetSiteTenantId", EnvironmentVariableTarget.Process);
            SubscriptionId = Environment.GetEnvironmentVariable("AzureWebJobsTargetSiteSubscriptionId", EnvironmentVariableTarget.Process);
            SiteResourceGroup = Environment.GetEnvironmentVariable("AzureWebJobsTargetSiteResourceGroup", EnvironmentVariableTarget.Process);
            VM = Environment.GetEnvironmentVariable("AzureWebJobsVM", EnvironmentVariableTarget.Process);
            FunctionsHostSlug = Environment.GetEnvironmentVariable("FunctionHostProjectSlug", EnvironmentVariableTarget.Process);
            PerformanceMeterSlug = Environment.GetEnvironmentVariable("PerformanceProjectSlug", EnvironmentVariableTarget.Process);
        }

        public string ClientId { get; set; }

        public string ClientSecret { get; set; }

        public string TenantId { get; set; }

        public string SubscriptionId { get; set; }

        public string SiteResourceGroup { get; set; }

        public string VM { get; set; }

        public string FunctionsHostSlug { get; set; }

        public string PerformanceMeterSlug { get; set; }

        public string ExtensionUrl { get; set; }

        public string AppUrl { get; set; }
    }
}
