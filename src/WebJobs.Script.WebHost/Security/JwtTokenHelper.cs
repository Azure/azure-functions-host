// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System;
using System.IdentityModel.Tokens.Jwt;
using Microsoft.Azure.WebJobs.Script.Config;
using Microsoft.IdentityModel.Tokens;

namespace Microsoft.Azure.WebJobs.Script.WebHost.Security
{
    internal static class JwtTokenHelper
    {
        public static string CreateToken(DateTime validUntil, string audience = null, string issuer = null, byte[] key = null)
        {
            var tokenHandler = new JwtSecurityTokenHandler();
            key = key ?? SecretsUtility.GetEncryptionKey();
            var tokenDescriptor = new SecurityTokenDescriptor
            {
                Audience = audience ?? ScriptConstants.AppServiceCoreUri,
                Issuer = issuer ?? string.Format(ScriptConstants.SiteUriFormat, ScriptSettingsManager.Instance.GetSetting(EnvironmentSettingNames.AzureWebsiteName)),
                Expires = validUntil,
                SigningCredentials = new SigningCredentials(new SymmetricSecurityKey(key), SecurityAlgorithms.HmacSha256Signature)
            };
            var token = tokenHandler.CreateToken(tokenDescriptor);
            string tokenValue = tokenHandler.WriteToken(token);

            return tokenValue;
        }
    }
}
