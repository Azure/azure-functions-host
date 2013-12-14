using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.ComponentModel.DataAnnotations;
using System.Linq;
using System.Web;

using Microsoft.WindowsAzure;
using Microsoft.WindowsAzure.Jobs.Dashboard.Infrastructure;

namespace Microsoft.WindowsAzure.Jobs.Dashboard.Configuration
{
    public class AppConfiguration
    {
        /// <summary>
        /// Gets the main storage account for the application
        /// </summary>
        //[Required]
        [TypeConverter(typeof(CloudStorageAccountTypeConverter))]
        public CloudStorageAccount MainStorage { get; set; }

        /// <summary>
        /// Gets the end point on which the web role is listening
        /// </summary>
        public Uri WebRoleEndpoint { get; set; }

        /// <summary>
        /// Gets the end point of the Antares Worker for Antares mode
        /// </summary>
        public Uri AntaresWorkerUrl { get; set; }

        /// <summary>
        /// Gets the password for this instance.
        /// </summary>
        public string LoginPassword { get; set; }

        /// <summary>
        /// Gets the URL to use for federated login
        /// </summary>
        public string FederatedLoginUrl { get; set; }
    }
}