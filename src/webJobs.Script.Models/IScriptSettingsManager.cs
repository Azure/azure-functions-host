using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace WebJobs.Script
{
    public interface IScriptSettingsManagner
    {
        string AzureWebsiteDefaultSubdomain { get; }
        string GetSetting(string key);
    }
}
