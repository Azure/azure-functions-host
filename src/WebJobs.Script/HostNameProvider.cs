// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;

namespace Microsoft.Azure.WebJobs.Script
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
    public class HostNameProvider
    {
        private readonly IEnvironment _environment;
        private string _hostName;

        public HostNameProvider(IEnvironment environment)
        {
            _environment = environment ?? throw new ArgumentNullException(nameof(environment));
        }

        /// <summary>
        /// Event raised when the hostname is updated (e.g., after a slot swap).
        /// Subscribers should use this to refresh any cached hostname-dependent data.
        /// </summary>
        public event EventHandler<HostNameChangedEventArgs> HostNameChanged;

        public virtual string Value
        {
            get
            {
                if (string.IsNullOrEmpty(_hostName))
                {
                    // default to the the value specified in environment
                    _hostName = _environment.GetEnvironmentVariable(EnvironmentSettingNames.AzureWebsiteHostName);
                    if (string.IsNullOrEmpty(_hostName))
                    {
                        // Linux Dedicated on AppService doesn't have WEBSITE_HOSTNAME
                        string websiteName = _environment.GetEnvironmentVariable(EnvironmentSettingNames.AzureWebsiteName);
                        if (!string.IsNullOrEmpty(websiteName))
                        {
                            _hostName = $"{websiteName}.azurewebsites.net";
                        }
                    }
                }
                return _hostName;
            }
        }

        public virtual void Synchronize(HttpRequest request, ILogger logger)
        {
            string hostNameHeaderValue = request.Headers[ScriptConstants.AntaresDefaultHostNameHeader];
            if (!string.IsNullOrEmpty(hostNameHeaderValue) &&
                string.Compare(Value, hostNameHeaderValue) != 0)
            {
                string previousHostName = Value;
                logger.LogInformation("HostName updated from '{0}' to '{1}'", previousHostName, hostNameHeaderValue);
                _hostName = hostNameHeaderValue;

                // Raise event to notify subscribers (e.g., FunctionsSyncManager) that hostname has changed.
                // This allows them to refresh any hostname-dependent cached data like invoke_url_template.
                OnHostNameChanged(previousHostName, hostNameHeaderValue);
            }
        }

        /// <summary>
        /// Raises the HostNameChanged event.
        /// </summary>
        protected virtual void OnHostNameChanged(string previousHostName, string newHostName)
        {
            HostNameChanged?.Invoke(this, new HostNameChangedEventArgs(previousHostName, newHostName));
        }

        internal void Reset()
        {
            _hostName = null;
        }
    }

    /// <summary>
    /// Event arguments for the HostNameChanged event.
    /// </summary>
    public class HostNameChangedEventArgs : EventArgs
    {
        public HostNameChangedEventArgs(string previousHostName, string newHostName)
        {
            PreviousHostName = previousHostName;
            NewHostName = newHostName;
        }

        /// <summary>
        /// Gets the previous hostname before the change.
        /// </summary>
        public string PreviousHostName { get; }

        /// <summary>
        /// Gets the new hostname after the change.
        /// </summary>
        public string NewHostName { get; }
    }
}
