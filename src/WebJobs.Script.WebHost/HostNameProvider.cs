// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using Microsoft.Azure.WebJobs.Host;
using Microsoft.Azure.WebJobs.Script.Config;

namespace Microsoft.Azure.WebJobs.Script.WebHost
{
    /// <summary>
    /// Provides the current HostName for the Function App.
    /// <remarks>
    /// The environment value for WEBSITE_HOSTNAME is unreliable and shouldn't be used directly. AppService site swaps change
    /// the site’s hostname under the covers, and the worker process is NOT recycled (for performance reasons). That means the
    /// site will continue to run with the same hostname environment variable, leading to an incorrect host name.
    ///
    /// WAS_DEFAULT_HOSTNAME is a header injected by front end on every request which provides the correct hostname. We check
    /// this header on all http requests, and updated the cached hostname value as needed.
    /// </remarks>
    /// </summary>
    public static class HostNameProvider
    {
        private static string _hostName;

        public static string Value
        {
            get
            {
                if (string.IsNullOrEmpty(_hostName))
                {
                    // default to the the value specified in environment
                    var settings = ScriptSettingsManager.Instance;
                    _hostName = settings.GetSetting(EnvironmentSettingNames.AzureWebsiteHostName);
                    if (string.IsNullOrEmpty(_hostName))
                    {
                        string websiteName = settings.GetSetting(EnvironmentSettingNames.AzureWebsiteName);
                        if (!string.IsNullOrEmpty(websiteName))
                        {
                            _hostName = $"{websiteName}.azurewebsites.net";
                        }
                    }
                }
                return _hostName;
            }
        }

        public static void Synchronize(HttpRequestMessage request, TraceWriter traceWriter)
        {
            string hostNameHeaderValue = request.GetHeaderValueOrDefault(ScriptConstants.AntaresDefaultHostNameHeader);
            if (!string.IsNullOrEmpty(hostNameHeaderValue) &&
                string.Compare(Value, hostNameHeaderValue) != 0)
            {
                traceWriter.InfoFormat("HostName updated from '{0}' to '{1}'", Value, hostNameHeaderValue);
                _hostName = hostNameHeaderValue;
            }
        }

        internal static void Reset()
        {
            _hostName = null;
        }
    }
}