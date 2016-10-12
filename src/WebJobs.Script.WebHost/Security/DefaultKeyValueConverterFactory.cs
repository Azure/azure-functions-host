// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Microsoft.Azure.WebJobs.Script.Config;
using static Microsoft.Azure.Web.DataProtection.Constants;

namespace Microsoft.Azure.WebJobs.Script.WebHost
{
    public sealed class DefaultKeyValueConverterFactory : IKeyValueConverterFactory
    {
        private bool _encryptionSupported;
        private static readonly PlaintextKeyValueConverter PlaintextValueConverter = new PlaintextKeyValueConverter(FileAccess.ReadWrite);
        private static ScriptSettingsManager _settingsManager;

        public DefaultKeyValueConverterFactory(ScriptSettingsManager settingsManager)
        {
            _settingsManager = settingsManager;
            _encryptionSupported = IsEncryptionSupported();
        }

        private static bool IsEncryptionSupported()
        {
            if (WebScriptHostManager.IsAzureEnvironment)
            {
                // We're temporarily placing encryption behind a feature toggle until
                // other consumers (e.g. portal) are updated to work with it.
                // TODO: Remove this
                return FeatureFlags.IsEnabled("SecretEncryption");
            }

            return _settingsManager.GetSetting(AzureWebsiteLocalEncryptionKey) != null;
        }

        public IKeyValueReader GetValueReader(Key key)
        {
            if (key.IsEncrypted)
            {
                return new DataProtectionKeyValueConverter(FileAccess.Read);
            }

            return PlaintextValueConverter;
        }

        public IKeyValueWriter GetValueWriter(Key key)
        {
            if (_encryptionSupported)
            {
                return new DataProtectionKeyValueConverter(FileAccess.Write);
            }

            return PlaintextValueConverter;
        }
    }
}