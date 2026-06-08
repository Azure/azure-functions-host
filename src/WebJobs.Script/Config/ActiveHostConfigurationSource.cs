// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System;
using System.Collections.Generic;
using Microsoft.Extensions.Configuration;

namespace Microsoft.Azure.WebJobs.Script.Configuration
{
    public class ActiveHostConfigurationSource(IScriptHostManager scriptHostManager) : IConfigurationSource
    {
        private readonly IScriptHostManager _scriptHostManager = scriptHostManager;

        public IConfigurationProvider Build(IConfigurationBuilder builder)
        {
            return new ActiveHostConfigurationProvider(_scriptHostManager);
        }

        private class ActiveHostConfigurationProvider : ConfigurationProvider, IDisposable
        {
            private readonly IScriptHostManager _scriptHostManager;
            private IDisposable _changeTokenRegistration;

            public ActiveHostConfigurationProvider(IScriptHostManager scriptHostManager)
            {
                ArgumentNullException.ThrowIfNull(scriptHostManager);
                _scriptHostManager = scriptHostManager;
                scriptHostManager.ActiveHostChanged += HandleActiveHostChange;
            }

            public override void Load()
            {
                if ((_scriptHostManager as IServiceProvider)?.GetService(typeof(IConfiguration))
                    is not IConfigurationRoot activeHostConfiguration)
                {
                    return;
                }

                Dictionary<string, string> data = new(StringComparer.OrdinalIgnoreCase);
                foreach (var kvp in activeHostConfiguration.AsEnumerable())
                {
                    if (!data.ContainsKey(kvp.Key))
                    {
                        data[kvp.Key] = kvp.Value;
                    }
                }

                Data = data;
                _changeTokenRegistration?.Dispose();
                _changeTokenRegistration = activeHostConfiguration.GetReloadToken().RegisterChangeCallback(_ => Load(), null);
                OnReload();
            }

            private void HandleActiveHostChange(object sender, ActiveHostChangedEventArgs e)
            {
                Load();
            }

            public void Dispose()
            {
                _changeTokenRegistration?.Dispose();
                _changeTokenRegistration = null;
                _scriptHostManager.ActiveHostChanged -= HandleActiveHostChange;
            }
        }
    }
}
