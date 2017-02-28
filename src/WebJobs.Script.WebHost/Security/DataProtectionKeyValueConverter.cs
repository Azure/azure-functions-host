// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.WebUtilities;
using Microsoft.Azure.Web.DataProtection;

namespace Microsoft.Azure.WebJobs.Script.WebHost
{
    public sealed class DataProtectionKeyValueConverter : KeyValueConverter, IKeyValueWriter, IKeyValueReader
    {
        private readonly IDataProtector _dataProtector;

        public DataProtectionKeyValueConverter(FileAccess access)
            : base(access)
        {
            var provider = DataProtectionProvider.CreateAzureDataProtector();
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

        //private static string GetKeyIdFromPayload(string encryptedValue)
        //{
        //    // Payload format details at:
        //    // https://docs.asp.net/en/latest/security/data-protection/implementation/authenticated-encryption-details.html

        //    byte[] encryptedPayload = WebEncoders.Base64UrlDecode(encryptedValue);

        //    if (encryptedValue.Length < 20)
        //    {
        //        throw new CryptographicException("Invalid cryptographic payload. Unable to extract key id.");
        //    }

        //    return new Guid(encryptedPayload.Skip(4).Take(16).ToArray()).ToString();
        //}
    }
}