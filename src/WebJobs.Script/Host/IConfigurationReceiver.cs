// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using Microsoft.Extensions.Configuration;

namespace Microsoft.Azure.WebJobs.Script.Host
{
    public interface IConfigurationReceiver
    {
        IConfiguration Configuration { set; }
    }
}
