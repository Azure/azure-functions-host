using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Threading.Tasks;
using Newtonsoft.Json;
using WebJobs.Script.Cli.Interfaces;

namespace WebJobs.Script.Cli.Common
{
    internal class SecretsManager : ISecretsManager
    {
        public const string SecretsFilePath = ".secrets";

        public IDictionary<string, string> GetSecrets()
        {
            try
            {
                var bytes = FileSystemHelpers.ReadAllBytes(Path.Combine(Environment.CurrentDirectory, SecretsFilePath));
                bytes = ProtectedData.Unprotect(bytes, null, DataProtectionScope.CurrentUser);
                var content = Encoding.UTF8.GetString(bytes);
                return JsonConvert.DeserializeObject<Dictionary<string, string>>(content);
            }
            catch(CryptographicException)
            {
                return new Dictionary<string, string>();
            }
        }

        public void SetSecret(string name, string value)
        {
            var secrets = GetSecrets();
            secrets[name] = value;
            var content = JsonConvert.SerializeObject(secrets);
            var bytes = Encoding.UTF8.GetBytes(content);
            bytes = ProtectedData.Protect(bytes, null, DataProtectionScope.CurrentUser);
            FileSystemHelpers.WriteAllBytes(Path.Combine(Environment.CurrentDirectory, SecretsFilePath), bytes);
        }
    }
}
