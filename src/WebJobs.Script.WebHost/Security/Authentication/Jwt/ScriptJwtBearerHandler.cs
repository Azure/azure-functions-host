// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System.Text.Encodings.Web;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.Azure.WebJobs.Script.Diagnostics;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace Microsoft.Azure.WebJobs.Script.WebHost.Security.Authentication.Jwt
{
    internal sealed class ScriptJwtBearerHandler : JwtBearerHandler
    {
        /// <summary>
        /// Initializes a new instance of the <see cref="ScriptJwtBearerHandler"/> class.
        /// </summary>
        /// <param name="options">The options.</param>
        /// <param name="encoder">The url encoder.</param>
        /// <param name="clock">The system clock.</param>
        /// <param name="loggerFactory">The system logger factory.</param>
        public ScriptJwtBearerHandler(
            IOptionsMonitor<JwtBearerOptions> options,
            UrlEncoder encoder,
            ISystemClock clock,
            ISystemLoggerFactory loggerFactory = null)
            : base(options, (ILoggerFactory)loggerFactory ?? NullLoggerFactory.Instance, encoder, clock)
        {
            // Note - ISystemLoggerFactory falls back to NullLoggerFactory to avoid needing this service in tests.
        }
    }
}
