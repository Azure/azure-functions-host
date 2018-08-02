﻿// Copyright (c) .NET Foundation. All rights reserved.
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
        private readonly bool _encryptionSupported;
        private static readonly PlaintextKeyValueConverter PlaintextValueConverter = new PlaintextKeyValueConverter(FileAccess.ReadWrite);
        private static ScriptSettingsManager _settingsManager;

        public DefaultKeyValueConverterFactory(ScriptSettingsManager settingsManager)
        {
            _settingsManager = settingsManager;
            _encryptionSupported = IsEncryptionSupported();
        }

        private static bool IsEncryptionSupported()
        {
            if (EnvironmentUtility.IsLinuxContainerEnvironment)
            {
                // TEMP: https://github.com/Azure/azure-functions-host/issues/3035
                return false;
            }

            return EnvironmentUtility.IsAppServiceEnvironment || _settingsManager.GetSetting(AzureWebsiteLocalEncryptionKey) != null;
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