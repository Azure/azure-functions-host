// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System;
using System.Collections.Generic;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using Colors.Net;
using Newtonsoft.Json;
using WebJobs.Script.Cli.Interfaces;
using static WebJobs.Script.Cli.Common.OutputTheme;

namespace WebJobs.Script.Cli.Common
{
    internal class SecretsManager : ISecretsManager
    {
        public const string SecretsFilePath = ".secrets";

        public IDictionary<string, string> GetSecrets()
        {
            return Try(() =>
            {
                var bytes = FileSystemHelpers.ReadAllBytes(Path.Combine(Environment.CurrentDirectory, SecretsFilePath));
                bytes = ProtectedData.Unprotect(bytes, null, DataProtectionScope.CurrentUser);
                var content = Encoding.UTF8.GetString(bytes);
                return JsonConvert.DeserializeObject<Dictionary<string, string>>(content);
            }, new Dictionary<string, string>());
        }

        public void SetSecret(string name, string value)
        {
            Try<object>(() =>
            {
                var secrets = GetSecrets();
                secrets[name] = value;
                var content = JsonConvert.SerializeObject(secrets);
                var bytes = Encoding.UTF8.GetBytes(content);
                bytes = ProtectedData.Protect(bytes, null, DataProtectionScope.CurrentUser);
                FileSystemHelpers.WriteAllBytes(Path.Combine(Environment.CurrentDirectory, SecretsFilePath), bytes);
                return null;
            }, null);
        }

        private static T Try<T>(Func<T> func, T @default)
        {
            try
            {
                return func();
            }
            catch (FileNotFoundException)
            {
                ColoredConsole
                    .Error
                    .WriteLine(ErrorColor($"Can not find file {SecretsFilePath}."))
                    .Write(ErrorColor($"Make sure you are in the root of your functions repo, and have ran"))
                    .Write(ErrorColor($" {ExampleColor("func init")} in there."))
                    .WriteLine();
                throw new CliException() { Handled = true };
            }
            catch (CryptographicException)
            {
                return @default;
            }
        }
    }
}
