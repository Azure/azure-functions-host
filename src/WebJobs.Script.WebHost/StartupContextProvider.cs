// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.Azure.WebJobs.Script.WebHost.Models;
using Microsoft.Azure.WebJobs.Script.WebHost.Security;
using Microsoft.Extensions.Logging;
using Newtonsoft.Json;

namespace Microsoft.Azure.WebJobs.Script.WebHost
{
    /// <summary>
    /// Class used for startup optimization, to provide access to values required at startup from
    /// efficient cache, rather than requiring external fetch operations.
    /// </summary>
    public class StartupContextProvider
    {
        private readonly IEnvironment _environment;
        private readonly ILogger _logger;
        private readonly object _syncLock = new object();
        private bool _loaded = false;
        private StartupContext _startupContext;

        public StartupContextProvider(IEnvironment environment, ILogger<StartupContextProvider> logger)
        {
            _environment = environment ?? throw new ArgumentNullException(nameof(environment));
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        }

        internal StartupContext Context
        {
            get
            {
                // context is only loaded once on startup
                if (!_loaded)
                {
                    lock (_syncLock)
                    {
                        if (!_loaded)
                        {
                            _startupContext = GetStartupContextOrNull();
                            _loaded = true;
                        }
                    }
                }
                return _startupContext;
            }

            set
            {
                lock (_syncLock)
                {
                    _startupContext = value;
                    _loaded = true;
                }
            }
        }

        public virtual HostSecretsInfo GetHostSecretsOrNull()
        {
            if (Context?.Secrets?.Host != null)
            {
                var hostSecrets = Context.Secrets.Host;
                var secretsInfo = new HostSecretsInfo
                {
                    MasterKey = hostSecrets.Master,
                    FunctionKeys = new Dictionary<string, string>(hostSecrets.Function, StringComparer.OrdinalIgnoreCase),
                    SystemKeys = new Dictionary<string, string>(hostSecrets.System, StringComparer.OrdinalIgnoreCase)
                };
                _logger.LogDebug("Loaded host keys from startup context");

                return secretsInfo;
            }

            return null;
        }

        public virtual IDictionary<string, IDictionary<string, string>> GetFunctionSecretsOrNull()
        {
            if (Context?.Secrets?.Function != null)
            {
                var functionKeys = Context.Secrets.Function.ToDictionary(p => p.Name, p => p.Secrets);

                _logger.LogDebug($"Loaded keys for {functionKeys.Keys.Count} functions from startup context");

                return functionKeys;
            }

            return null;
        }

        /// <summary>
        /// Load context from local file system if specified.
        /// </summary>
        /// <returns>The context.</returns>
        private StartupContext GetStartupContextOrNull()
        {
            var contextPath = _environment.GetEnvironmentVariable(EnvironmentSettingNames.AzureWebsiteStartupContextCache);
            if (!string.IsNullOrEmpty(contextPath))
            {
                try
                {
                    contextPath = Environment.ExpandEnvironmentVariables(contextPath);
                    _logger.LogDebug($"Loading startup context from {contextPath}");
                    string content = File.ReadAllText(contextPath);

                    // Context files are onetime use. We delete after reading to ensure
                    // that we don't use a stale file in the future if the app recycles, etc.
                    // Dont' want to block on this file operation, so we kick it off in the background.
                    Task.Run(() => File.Delete(contextPath));

                    string decryptedContent = SimpleWebTokenHelper.Decrypt(content, environment: _environment);
                    var context = JsonConvert.DeserializeObject<StartupContext>(decryptedContent);

                    return context;
                }
                catch (Exception ex)
                {
                    // best effort
                    _logger.LogError(ex, "Failed to load startup context");
                    return null;
                }
            }
            else
            {
                return null;
            }
        }

        /// <summary>
        /// Decrypt and deserialize the specified context, and apply values from it to the
        /// startup cache context.
        /// </summary>
        /// <param name="encryptedContext">The encrypted assignment context.</param>
        /// <returns>The decrypted assignment context</returns>
        public virtual HostAssignmentContext SetContext(EncryptedHostAssignmentContext encryptedContext)
        {
            string decryptedContext = SimpleWebTokenHelper.Decrypt(encryptedContext.EncryptedContext, environment: _environment);
            var hostAssignmentContext = JsonConvert.DeserializeObject<HostAssignmentContext>(decryptedContext);

            // Don't update StartupContext for warmup requests
            if (!hostAssignmentContext.IsWarmupRequest)
            {
                // apply values from the context to our cached context
                Context = new StartupContext
                {
                    Secrets = hostAssignmentContext.Secrets
                };
            }

            return hostAssignmentContext;
        }

        public class StartupContext
        {
            [JsonProperty("secrets")]
            public FunctionAppSecrets Secrets { get; set; }
        }
    }
}
