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
        private readonly bool _shouldEncrypt;

        /// <summary>
        /// Indicates whether the repository owner validated shared Azure key material.
        /// </summary>
        private readonly bool _useAzureKeyRepository;
        private static readonly PlaintextKeyValueConverter PlaintextValueConverter = new PlaintextKeyValueConverter(FileAccess.ReadWrite);

        /// <summary>
        /// Initializes a factory using the existing hosting-environment detection.
        /// </summary>
        /// <param name="repositorySupportsEncryption">Whether the repository provides its own encryption.</param>
        public DefaultKeyValueConverterFactory(bool repositorySupportsEncryption)
            : this(repositorySupportsEncryption, useAzureKeyRepository: false)
        {
        }

        /// <summary>
        /// Initializes a factory for a repository with validated shared Azure key material.
        /// </summary>
        /// <param name="repositorySupportsEncryption">Whether the repository provides its own encryption.</param>
        /// <param name="useAzureKeyRepository">Whether to explicitly activate the Azure key repository.</param>
        internal DefaultKeyValueConverterFactory(bool repositorySupportsEncryption, bool useAzureKeyRepository)
        {
            _useAzureKeyRepository = useAzureKeyRepository;
            _shouldEncrypt = !repositorySupportsEncryption && (useAzureKeyRepository || IsEncryptionSupported());
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
                return new DataProtectionKeyValueConverter(FileAccess.Read, useAzureKeyRepository: _useAzureKeyRepository);
            }

            return PlaintextValueConverter;
        }

        public IKeyValueWriter GetValueWriter(Key key)
        {
            if (_shouldEncrypt)
            {
                return new DataProtectionKeyValueConverter(FileAccess.Write, useAzureKeyRepository: _useAzureKeyRepository);
            }

            return PlaintextValueConverter;
        }
    }
}