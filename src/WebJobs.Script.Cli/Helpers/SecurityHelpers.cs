// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System;
using System.Collections.Generic;
using System.Security.Cryptography.X509Certificates;
using System.Security.Principal;
#if WINDOWS
using CERTENROLLLib;
#endif
using Ignite.SharpNetSH;
using Microsoft.Azure.WebJobs.Script;
using WebJobs.Script.Cli.NativeMethods;

namespace WebJobs.Script.Cli.Helpers
{
    internal static class SecurityHelpers
    {
        public static bool IsSSLConfigured(int port)
        {
            return NetSH.CMD.Http.Show.SSLCert($"0.0.0.0:{port}")?.ResponseObject?.Count > 0;
        }

        [System.Diagnostics.CodeAnalysis.SuppressMessage("Microsoft.Maintainability", "CA1502:AvoidExcessiveComplexity")]
        public static bool IsUrlAclConfigured(string protocol, int port)
        {
            if (!PlatformHelper.IsWindows)
            {
                return true;
            }

            var responses = NetSH.CMD.Http.Show.UrlAcl($"{protocol}://+:{port}/")?.ResponseObject;
            if (responses?.Count > 0)
            {
                var response = responses[0];
                if (response != null)
                {
                    if (HasProperty(response, "User") && CheckCurrentUser(response))
                    {
                        return true;
                    }

                    if (HasProperty(response, "Users"))
                    {
                        foreach (var user in response.Users)
                        {
                            if (CheckCurrentUser(user)) return true;
                        }
                    }
                }
            }
            return false;
        }

        private static bool CheckCurrentUser(dynamic obj)
        {
            if (obj != null && HasProperty(obj, "User"))
            {
                return $"{Environment.UserDomainName}\\{Environment.UserName}".Equals(obj.User.User.ToString(), StringComparison.OrdinalIgnoreCase);
            }
            return false;
        }

        private static bool HasProperty(dynamic obj, string propertyName)
        {
            var dictionary = obj as IDictionary<string, object>;
            return dictionary != null && dictionary.ContainsKey(propertyName);
        }

        public static bool IsAdministrator()
        {
            WindowsIdentity identity = WindowsIdentity.GetCurrent();
            WindowsPrincipal principal = new WindowsPrincipal(identity);
            return principal.IsInRole(WindowsBuiltInRole.Administrator);
        }

        public static string ReadPassword()
        {
            var password = ConsoleNativeMethods.ReadPassword();
            System.Console.WriteLine();
            return password;
        }

        // http://stackoverflow.com/a/13806300/3234163
        public static X509Certificate2 CreateSelfSignedCertificate(string subjectName)
        {
#if WINDOWS
            // create DN for subject and issuer
            var dn = new CX500DistinguishedName();
            dn.Encode("CN=" + subjectName, X500NameFlags.XCN_CERT_NAME_STR_NONE);

            // create a new private key for the certificate
            CX509PrivateKey privateKey = new CX509PrivateKey();
            privateKey.ProviderName = "Microsoft Base Cryptographic Provider v1.0";
            privateKey.MachineContext = true;
            privateKey.Length = 2048;
            privateKey.KeySpec = X509KeySpec.XCN_AT_SIGNATURE; // use is not limited
            privateKey.ExportPolicy = X509PrivateKeyExportFlags.XCN_NCRYPT_ALLOW_PLAINTEXT_EXPORT_FLAG;
            privateKey.Create();

            // Use the stronger SHA512 hashing algorithm
            var hashobj = new CObjectId();
            hashobj.InitializeFromAlgorithmName(ObjectIdGroupId.XCN_CRYPT_HASH_ALG_OID_GROUP_ID,
                ObjectIdPublicKeyFlags.XCN_CRYPT_OID_INFO_PUBKEY_ANY,
                AlgorithmFlags.AlgorithmFlagsNone, "SHA512");

            // add extended key usage if you want - look at MSDN for a list of possible OIDs
            var oid = new CObjectId();
            oid.InitializeFromValue("1.3.6.1.5.5.7.3.1"); // SSL server
            var oidlist = new CObjectIds();
            oidlist.Add(oid);
            var eku = new CX509ExtensionEnhancedKeyUsage();
            eku.InitializeEncode(oidlist);

            // Create the self signing request
            var cert = new CX509CertificateRequestCertificate();
            cert.InitializeFromPrivateKey(X509CertificateEnrollmentContext.ContextMachine, privateKey, "");
            cert.Subject = dn;
            cert.Issuer = dn; // the issuer and the subject are the same
            cert.NotBefore = DateTime.Now;
            cert.NotAfter = DateTime.Now.AddYears(5);
            cert.X509Extensions.Add((CX509Extension)eku); // add the EKU
            cert.HashAlgorithm = hashobj; // Specify the hashing algorithm
            cert.Encode(); // encode the certificate

            // Do the final enrollment process
            var enroll = new CX509Enrollment();
            enroll.InitializeFromRequest(cert); // load the certificate
            enroll.CertificateFriendlyName = subjectName; // Optional: add a friendly name
            string csr = enroll.CreateRequest(); // Output the request in base64
                                                 // and install it back as the response
            enroll.InstallResponse(InstallResponseRestrictionFlags.AllowUntrustedCertificate,
                csr, EncodingType.XCN_CRYPT_STRING_BASE64, string.Empty); // no password
                                                                // output a base64 encoded PKCS#12 so we can import it back to the .Net security classes
            var base64encoded = enroll.CreatePFX(string.Empty, // no password, this is for internal consumption
                PFXExportOptions.PFXExportChainWithRoot);

            // instantiate the target class with the PKCS#12 data (and the empty password)
            return new X509Certificate2(
                Convert.FromBase64String(base64encoded), string.Empty,
                // mark the private key as exportable (this is usually what you want to do)
                X509KeyStorageFlags.Exportable | X509KeyStorageFlags.MachineKeySet | X509KeyStorageFlags.PersistKeySet);
#else
            throw new NotSupportedException();
#endif
        }
    }
}
