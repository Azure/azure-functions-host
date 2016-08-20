// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

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
