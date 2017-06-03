using Microsoft.Azure.WebJobs.Script.Config;
using Microsoft.Azure.WebJobs.Script.WebHost;
using System;
using System.Collections.Generic;
using System.Fabric;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Microsoft.Azure.WebJobs.Script.FabricHost
{
    class FabricServiceSettings
    {
        internal static WebHostSettings CreateDefault(ScriptSettingsManager settingsManager, ServiceContext serviceContext)
        {
            WebHostSettings settings = new WebHostSettings();

            // get the data packages
            var secretsPackage = serviceContext.CodePackageActivationContext.GetDataPackageObject("Secrets");
            var scriptsPackage = serviceContext.CodePackageActivationContext.GetDataPackageObject("Scripts");

            // set to Service Fabric specific values
            settings.IsSelfHost = true;
            settings.ScriptPath = scriptsPackage.Path;
            settings.LogPath = Path.Combine(serviceContext.CodePackageActivationContext.LogDirectory, @"Functions");
            settings.SecretsPath = secretsPackage.Path;

            if (string.IsNullOrEmpty(settings.ScriptPath))
            {
                throw new InvalidOperationException("Unable to determine function script root directory.");
            }

            return settings;

        }
    }
}
