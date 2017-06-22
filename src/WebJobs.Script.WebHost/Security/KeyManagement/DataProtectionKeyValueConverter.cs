// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.WebUtilities;

namespace Microsoft.Azure.WebJobs.Script.WebHost
{
    public sealed class DataProtectionKeyValueConverter : KeyValueConverter, IKeyValueWriter, IKeyValueReader
    {
        private readonly IDataProtector _dataProtector;

        public DataProtectionKeyValueConverter(FileAccess access)
            : base(access)
        {
            var provider = Web.DataProtection.DataProtectionProvider.CreateAzureDataProtector();
            _dataProtector = provider.CreateProtector("function-secrets");
        }

        public Key ReadValue(Key key)
        {
            ValidateAccess(FileAccess.Read);

            return Unprotect(key);
        }

        private Key Unprotect(Key key)
        {
            var resultKey = new Key(key.Name, null);

            var protector = _dataProtector as IPersistedDataProtector;

            if (protector != null)
            {
                bool wasRevoked, requiresMigration;
                byte[] data = WebEncoders.Base64UrlDecode(key.Value);
                byte[] result = protector.DangerousUnprotect(data, false, out requiresMigration, out wasRevoked);

                resultKey.Value = Encoding.UTF8.GetString(result);
                resultKey.IsStale = requiresMigration;
            }
            else
            {
                resultKey.Value = _dataProtector.Unprotect(key.Value);
            }

            return resultKey;
        }

        public Key WriteValue(Key key)
        {
            ValidateAccess(FileAccess.Write);

            string encryptedValue = _dataProtector.Protect(key.Value);

            return new Key
            {
                Name = key.Name,
                Value = encryptedValue,
                IsEncrypted = true
            };
        }
    }
}