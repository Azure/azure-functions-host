// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

namespace Microsoft.Azure.WebJobs.Script.WebHost.Extensions
{
    public static class WebHostSettingsExtensions
    {
        public static ScriptHostConfiguration ToScriptHostConfiguration(this WebHostSettings webHostSettings) =>
            WebHostResolver.CreateScriptHostConfiguration(webHostSettings);
    }
}