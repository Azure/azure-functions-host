// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System;
using System.Diagnostics.CodeAnalysis;
using System.Linq;
using Microsoft.Win32;

namespace Microsoft.Azure.WebJobs.Script.Scaling
{
    public static class AppServiceSettings
    {
        private static bool? _runtimeScalingEnabled;
        private static string _siteName;
        private static string _hostName;
        private static string _homeStampName;
        private static string _currentStampName;
        private static string _workerName;
        private static string _storageConnectionString;
        private static string _sku;
        private static string _lockHandleId;
        private static bool? _validateCertificates;
        private static byte[] _runtimeEncryptionKey;

        /// <summary>
        /// Gets or sets a value indicating whether runtime scaling enabled
        /// </summary>
        public static bool? RuntimeScalingEnabled
        {
            get
            {
                if (_runtimeScalingEnabled == null)
                {
                    _runtimeScalingEnabled = Environment.GetEnvironmentVariable("WEBSITE_RUNTIME_SCALING_ENABLED") == "1";
                }

                return _runtimeScalingEnabled;
            }

            set
            {
                _runtimeScalingEnabled = value;
            }
        }

        /// <summary>
        /// Gets or sets runtime site name
        /// </summary>
        public static string SiteName
        {
            get
            {
                if (_siteName == null)
                {
                    // this is runtime time siteName as far as Azure is concerned
                    var runtimeSiteName = Environment.GetEnvironmentVariable("WEBSITE_IIS_SITE_NAME")?.ToLowerInvariant();
                    if (runtimeSiteName != null && runtimeSiteName.StartsWith("~1"))
                    {
                        _siteName = runtimeSiteName.Substring(2);
                    }
                    else
                    {
                        _siteName = runtimeSiteName;
                    }
                }

                return _siteName;
            }

            set
            {
                _siteName = value;
            }
        }

        /// <summary>
        /// Gets or sets site host name
        /// </summary>
        public static string HostName
        {
            get
            {
                if (_hostName == null)
                {
                    _hostName = Environment.GetEnvironmentVariable("WEBSITE_HOSTNAME")?.ToLowerInvariant();
                }

                return _hostName;
            }

            set
            {
                _hostName = value;
            }
        }

        /// <summary>
        /// Gets or sets site home stamp name
        /// </summary>
        public static string HomeStampName
        {
            get
            {
                if (_homeStampName == null)
                {
                    _homeStampName = Environment.GetEnvironmentVariable("WEBSITE_HOME_STAMPNAME")?.ToLowerInvariant();
                }

                return _homeStampName;
            }

            set
            {
                _homeStampName = value;
            }
        }

        /// <summary>
        /// Gets or sets current worker stamp name
        /// </summary>
        public static string CurrentStampName
        {
            get
            {
                if (_currentStampName == null)
                {
                    _currentStampName = Environment.GetEnvironmentVariable("WEBSITE_CURRENT_STAMPNAME")?.ToLowerInvariant();
                }

                return _currentStampName;
            }

            set
            {
                _currentStampName = value;
            }
        }

        /// <summary>
        /// Gets or sets current worker name
        /// </summary>
        public static string WorkerName
        {
            get
            {
                if (_workerName == null)
                {
                    using (var key = Registry.LocalMachine.OpenSubKey(@"SOFTWARE\Microsoft\IIS Extensions\DwasMod"))
                    {
                        _workerName = ((string)key?.GetValue("IpAddress"))?.ToLowerInvariant();
                    }
                }

                return _workerName;
            }

            set
            {
                _workerName = value;
            }
        }

        /// <summary>
        /// Gets or sets validate SSL certificate
        /// </summary>
        public static bool? ValidateCertificates
        {
            get
            {
                if (_validateCertificates == null)
                {
                    using (var key = Registry.LocalMachine.OpenSubKey(@"SOFTWARE\Microsoft\IIS Extensions\DwasMod"))
                    {
                        _validateCertificates = (int)key.GetValue("ValidateCertificates") != 0;
                    }
                }

                return _validateCertificates;
            }

            set
            {
                _validateCertificates = value;
            }
        }

        /// <summary>
        /// Gets or sets current storage connection string
        /// </summary>
        public static string StorageConnectionString
        {
            get
            {
                if (_storageConnectionString == null)
                {
                    _storageConnectionString = Environment.GetEnvironmentVariable("AzureWebJobsStorage");
                }

                return _storageConnectionString;
            }

            set
            {
                _storageConnectionString = value;
            }
        }

        /// <summary>
        /// Gets or sets site sku
        /// </summary>
        [SuppressMessage("Microsoft.Naming", "CA1704:IdentifiersShouldBeSpelledCorrectly", MessageId = "Sku")]
        public static string Sku
        {
            get
            {
                if (_sku == null)
                {
                    _sku = Environment.GetEnvironmentVariable("WEBSITE_SKU");
                }

                return _sku;
            }

            set
            {
                _sku = value;
            }
        }

        /// <summary>
        /// Gets or sets lock handle id which is stable for each worker
        /// </summary>
        public static string LockHandleId
        {
            get
            {
                if (_lockHandleId == null)
                {
                    var lockHandleId = Environment.GetEnvironmentVariable("WEBSITE_INSTANCE_ID")
                        ?? Environment.MachineName.GetHashCode().ToString("X").PadLeft(32, '0');

                    _lockHandleId = lockHandleId.Substring(0, 32);
                }

                return _lockHandleId;
            }

            set
            {
                _lockHandleId = value;
            }
        }

        /// <summary>
        /// Gets or sets a runtime encryption key
        /// </summary>
        public static byte[] RuntimeEncryptionKey
        {
            get
            {
                if (_runtimeEncryptionKey == null)
                {
                    var value = Environment.GetEnvironmentVariable("WEBSITE_AUTH_ENCRYPTION_KEY");
                    if (string.IsNullOrEmpty(value))
                    {
                        throw new InvalidOperationException("MIssing WEBSITE_AUTH_ENCRYPTION_KEY environment variable");
                    }

                    try
                    {
                        // only support 32 bytes (256 bits) key length
                        // either hex or base64 string format
                        if (value.Length == 64)
                        {
                            _runtimeEncryptionKey = Enumerable.Range(0, value.Length)
                                             .Where(x => x % 2 == 0)
                                             .Select(x => Convert.ToByte(value.Substring(x, 2), 16))
                                             .ToArray();
                        }
                        else
                        {
                            _runtimeEncryptionKey = Convert.FromBase64String(value);
                        }
                    }
                    catch (Exception ex)
                    {
                        throw new InvalidOperationException(string.Format("Invalid base64 WEBSITE_AUTH_ENCRYPTION_KEY environment variable '{0}'.", value), ex);
                    }
                }

                return _runtimeEncryptionKey;
            }

            set
            {
                _runtimeEncryptionKey = value;
            }
        }

        public static string ManagerPartitionKey
        {
            get { return string.Format("{0}(manager)", AppServiceSettings.SiteName); }
        }

        public static string ManagerRowKey
        {
            get { return SiteName; }
        }

        public static string WorkerPartitionKey
        {
            get { return SiteName; }
        }

        public static string GetWorkerRowKey(string stampName, string workerName)
        {
            return string.Format("{0}:{1}", stampName, workerName);
        }
    }
}