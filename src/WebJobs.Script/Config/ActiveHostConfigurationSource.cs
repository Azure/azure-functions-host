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

        private class ActiveHostConfigurationProvider : ConfigurationProvider, IDisposable
        {
            private readonly IScriptHostManager _scriptHostManager;
            private IDisposable _changeTokenRegistration;

            public ActiveHostConfigurationProvider(IScriptHostManager scriptHostManager)
            {
                _scriptHostManager = scriptHostManager ?? throw new ArgumentNullException(nameof(scriptHostManager));
                scriptHostManager.ActiveHostChanged += HandleActiveHostChange;
            }

            public override void Load()
            {
                if ((_scriptHostManager as IServiceProvider)?.GetService(typeof(IConfiguration)) is IConfigurationRoot activeHostConfiguration)
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

            public void Dispose()
            {
                _changeTokenRegistration?.Dispose();
                _scriptHostManager.ActiveHostChanged -= HandleActiveHostChange;
            }
        }
    }
}
