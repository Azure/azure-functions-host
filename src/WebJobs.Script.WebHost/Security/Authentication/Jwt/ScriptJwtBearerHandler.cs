// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System.Text.Encodings.Web;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.Azure.WebJobs.Script.Diagnostics;
using Microsoft.Extensions.Options;

namespace Microsoft.Azure.WebJobs.Script.WebHost.Security.Authentication.Jwt
{
    internal sealed class ScriptJwtBearerHandler : JwtBearerHandler
    {
        /// <summary>
        /// Initializes a new instance of the <see cref="ScriptJwtBearerHandler"/> class.
        /// </summary>
        /// <param name="options">The options.</param>
        /// <param name="loggerFactory">The system logger factory.</param>
        /// <param name="encoder">The url encoder.</param>
        /// <param name="clock">The system clock.</param>
        public ScriptJwtBearerHandler(IOptionsMonitor<JwtBearerOptions> options, ISystemLoggerFactory loggerFactory, UrlEncoder encoder, ISystemClock clock)
            : base(options, loggerFactory, encoder, clock)
        {
            // Note - we provide a NullLoggerFactory to suppress from customer logs.
        }
    }
}
