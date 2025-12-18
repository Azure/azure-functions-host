using Microsoft.Azure.WebJobs.Script;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Options;

namespace Microsoft.Azure.Functions.InstanceManager.Configuration;

// TODO (worker refactor): Move helpers to common class if needed
internal sealed class WorkerProcessOptionsSetup : IConfigureOptions<WorkerProcessOptions>
{
    private readonly IConfiguration _configuration;

    public WorkerProcessOptionsSetup(IConfiguration configuration)
    {
        _configuration = configuration;
    }

    public void Configure(WorkerProcessOptions options)
    {
        options.AssignUserExecutePermissionsIfNotExists = IsAnyLinuxConsumption();
    }

    private bool IsAnyLinuxConsumption()
    {
        return IsLinuxConsumptionOnAtlas() ||
               IsFlexConsumptionSku() ||
               IsLinuxConsumptionOnLegion();
    }

    private bool IsLinuxConsumptionOnLegion()
    {
        return IsConsumptionOnLegion() && WebsiteSkuIsDynamic();
    }

    public bool IsLinuxConsumptionOnAtlas()
    {
        return !IsAppService() &&
               !string.IsNullOrEmpty(_configuration.GetValue<string>(EnvironmentSettingNames.ContainerName)) &&
               string.IsNullOrEmpty(_configuration.GetValue<string>(EnvironmentSettingNames.LegionServiceHost));
    }

    private bool IsAppService()
    {
        return !string.IsNullOrEmpty(_configuration.GetValue<string>(EnvironmentSettingNames.AzureWebsiteInstanceId));
    }

    public bool IsConsumptionOnLegion()
    {
        return !IsAppService() &&
               (!string.IsNullOrEmpty(_configuration.GetValue<string>(EnvironmentSettingNames.ContainerName)) ||
               !string.IsNullOrEmpty(_configuration.GetValue<string>(EnvironmentSettingNames.WebsitePodName))) &&
               !string.IsNullOrEmpty(_configuration.GetValue<string>(EnvironmentSettingNames.LegionServiceHost));
    }

    public bool IsFlexConsumptionSku()
    {
        // SKU is part of Flex Consumption placeholder environment vars, so should always be present
        string value = _configuration.GetValue<string>(EnvironmentSettingNames.AzureWebsiteSku);
        if (string.Equals(value, ScriptConstants.FlexConsumptionSku, StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        // In the past, SKU was not set in placeholder mode, so the following additional
        // checks were performed. May not be needed anymore.
        if (!IsConsumptionOnLegion())
        {
            // not running on Legion, so not Flex Consumption
            return false;
        }

        // If we're running on Legion and the app isn't CV1 on Legion,
        // then it's Flex
        return !WebsiteSkuIsDynamic();
    }

    private bool WebsiteSkuIsDynamic()
    {
        string value = _configuration.GetValue<string>(EnvironmentSettingNames.AzureWebsiteSku);
        if (string.Equals(value, ScriptConstants.DynamicSku, StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        // Linux Consumption uses WEBSITE_SKU_NAME but is migrating to use WEBSITE_SKU.
        // So for now, we must check both.
        value = _configuration.GetValue<string>(EnvironmentSettingNames.AzureWebsiteSkuName);
        if (string.Equals(value, ScriptConstants.DynamicSku, StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        return false;
    }
}
