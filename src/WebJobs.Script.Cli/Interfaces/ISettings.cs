using System.Collections.Generic;

namespace WebJobs.Script.Cli.Interfaces
{
    internal interface ISettings
    {
        bool DisplayLaunchingRunServerWarning { get; set; }

        bool RunFirstTimeCliExperience { get; set; }

        Dictionary<string, object> GetSettings();

        void SetSetting(string name, string value);
    }
}
