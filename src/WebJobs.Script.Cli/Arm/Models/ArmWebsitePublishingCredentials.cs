// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using Newtonsoft.Json;

namespace WebJobs.Script.Cli.Arm.Models
{
    internal class ArmWebsitePublishingCredentials
    {
        [JsonProperty("publishingUserName")]
        public string PublishingUserName { get; set; }

        [JsonProperty("publishingPassword")]
        public string PublishingPassword { get; set; }

        [JsonProperty("scmUri")]
        public string ScmUri { get; set; }
    }
}