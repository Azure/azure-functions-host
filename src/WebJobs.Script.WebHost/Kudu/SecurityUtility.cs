using System;
using System.Security.Cryptography;

namespace Microsoft.Azure.WebJobs.Script.WebHost.Kudu
{
    public class SecurityUtility
    {
        public static string GenerateSecretString()
        {
            using (var rng = RandomNumberGenerator.Create())
            {
                byte[] data = new byte[40];
                rng.GetBytes(data);
                string secret = Convert.ToBase64String(data);

                // Replace pluses as they are problematic as URL values
                return secret.Replace('+', 'a');
            }
        }
    }
}
