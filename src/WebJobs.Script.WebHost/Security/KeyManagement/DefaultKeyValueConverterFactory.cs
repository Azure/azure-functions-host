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
        private readonly bool _encryptionSupported;

        /// <summary>
        /// Forces Azure Websites Data Protection configuration; otherwise uses existing hosting-environment detection.
        /// </summary>
        private readonly bool _forceAzureWebsitesDataProtection;
        private static readonly PlaintextKeyValueConverter PlaintextValueConverter = new PlaintextKeyValueConverter(FileAccess.ReadWrite);

        public DefaultKeyValueConverterFactory(bool allowEncryption)
            : this(allowEncryption, forceAzureWebsitesDataProtection: false)
        {
        }

        /// <summary>
        /// Initializes a factory with optional forced Azure Websites Data Protection configuration.
        /// </summary>
        /// <param name="allowEncryption">Whether the repository provides its own encryption.</param>
        /// <param name="forceAzureWebsitesDataProtection">Whether to force Azure Websites Data Protection configuration instead of hosting-environment detection.</param>
        internal DefaultKeyValueConverterFactory(bool allowEncryption, bool forceAzureWebsitesDataProtection)
        {
            _forceAzureWebsitesDataProtection = forceAzureWebsitesDataProtection;
            _encryptionSupported = !allowEncryption && IsEncryptionSupported();
        }

        private static bool IsEncryptionSupported()
        {
            return SystemEnvironment.Instance.IsAppService() ||
                SystemEnvironment.Instance.IsAnyLinuxConsumption() ||
                SystemEnvironment.Instance.GetEnvironmentVariable(AzureWebsiteLocalEncryptionKey) != null;
        }

        public IKeyValueReader GetValueReader(Key key)
        {
            if (key.IsEncrypted)
            {
                return new DataProtectionKeyValueConverter(FileAccess.Read, forceAzureWebsitesDataProtection: _forceAzureWebsitesDataProtection);
            }

            return PlaintextValueConverter;
        }

        public IKeyValueWriter GetValueWriter(Key key)
        {
            if (_encryptionSupported)
            {
                return new DataProtectionKeyValueConverter(FileAccess.Write, forceAzureWebsitesDataProtection: _forceAzureWebsitesDataProtection);
            }

            return PlaintextValueConverter;
        }
    }
}