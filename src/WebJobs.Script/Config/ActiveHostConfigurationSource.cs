// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System;
using System.Collections.Generic;
using Microsoft.Extensions.Configuration;

namespace Microsoft.Azure.WebJobs.Script.Configuration
{
    public class ActiveHostConfigurationSource : IConfigurationSource
    {
        private readonly IScriptHostManager _scriptHostManager;

        public ActiveHostConfigurationSource(IScriptHostManager scriptHostManager)
        {
            _scriptHostManager = scriptHostManager;
        }

        public IConfigurationProvider Build(IConfigurationBuilder builder)
        {
            return new ActiveHostConfigurationProvider(_scriptHostManager);
        }

        private class ActiveHostConfigurationProvider : ConfigurationProvider
        {
            private readonly IServiceProvider _serviceProvider;
            private IDisposable _changeTokenRegistration;

            public ActiveHostConfigurationProvider(IScriptHostManager scriptHostManager)
            {
                _ = scriptHostManager ?? throw new ArgumentNullException(nameof(scriptHostManager));
                _serviceProvider = scriptHostManager as IServiceProvider;
                scriptHostManager.ActiveHostChanged += HandleActiveHostChange;
            }

            public override void Load()
            {
                if (_serviceProvider?.GetService(typeof(IConfiguration)) is IConfigurationRoot activeHostConfiguration)
                {
                    Data = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
                    foreach (var kvp in activeHostConfiguration.AsEnumerable())
                    {
                        if (!Data.ContainsKey(kvp.Key))
                        {
                            Data[kvp.Key] = kvp.Value;
                        }
                    }

                    _changeTokenRegistration?.Dispose();
                    _changeTokenRegistration = activeHostConfiguration.GetReloadToken().RegisterChangeCallback(_ => Load(), null);
                    OnReload();
                }
            }

            private void HandleActiveHostChange(object sender, ActiveHostChangedEventArgs e)
            {
                Load();
            }
        }
    }
}
