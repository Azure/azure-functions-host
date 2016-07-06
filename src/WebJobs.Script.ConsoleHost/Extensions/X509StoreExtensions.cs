// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System.Security.Cryptography.X509Certificates;

namespace WebJobs.Script.ConsoleHost.Extensions
{
    public static class X509StoreExtensions
    {
        public static void AddCert(this X509Store store, X509Certificate2 cert)
        {
            store.Open(OpenFlags.MaxAllowed);
            store.Add(cert);
            store.Close();
        }
    }
}
