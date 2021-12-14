// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using BenchmarkDotNet.Attributes;
using Microsoft.Azure.WebJobs.Extensions.Http;
using Microsoft.Azure.WebJobs.Script.WebHost.Security.Authentication;
using Microsoft.Azure.WebJobs.Script.WebHost.Security.Authorization;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Security.Claims;

namespace Microsoft.Azure.WebJobs.Script.Benchmarks
{
    public class AuthUtilityBenchmarks
    {
        private ClaimsPrincipal Principal;
        private static List<Claim> TotallyRandomClaims { get; } = new List<Claim>()
        {
            new Claim(SecurityConstants.AuthLevelKeyNameClaimType, "test1"),
            new Claim(SecurityConstants.AuthLevelKeyNameClaimType, "test2"),
            new Claim(SecurityConstants.AuthLevelKeyNameClaimType, "test3"),
            new Claim(SecurityConstants.AuthLevelClaimType, nameof(AuthorizationLevel.Function)),
            new Claim(SecurityConstants.AuthLevelClaimType, nameof(AuthorizationLevel.Anonymous)),
            new Claim(SecurityConstants.AuthLevelClaimType, nameof(AuthorizationLevel.User)),
            new Claim(SecurityConstants.AuthLevelClaimType, nameof(AuthorizationLevel.Admin)),
            new Claim(SecurityConstants.AuthLevelClaimType, nameof(AuthorizationLevel.System)),
        };

        [Params(null, "code")]
        public string KeyName;

        [Params(0, 4, 8)]
        public int ClaimsCount;

        [GlobalSetup]
        public void Setup()
        {
            var identity = new ClaimsIdentity(TotallyRandomClaims.Take(ClaimsCount));
            Principal = new ClaimsPrincipal(identity);
        }

        [Benchmark(Baseline = true)]
        public bool PrincipalHasAuthLevelClaim() =>
            AuthUtility.PrincipalHasAuthLevelClaim(Principal, AuthorizationLevel.Function);

        [Benchmark]
        public bool PrincipalHasAuthLevelClaimNoArray() =>
            PrincipalHasAuthLevelClaimNoArray(Principal, AuthorizationLevel.Function);

        [Benchmark]
        public bool PrincipalHasAuthLevelClaimHasClaim() =>
            PrincipalHasAuthLevelClaimHasClaim(Principal, AuthorizationLevel.Function);

        public static bool PrincipalHasAuthLevelClaimNoArray(ClaimsPrincipal principal, AuthorizationLevel requiredLevel, string keyName = null)
        {
            // If the required auth level is anonymous, the requirement is met
            if (requiredLevel == AuthorizationLevel.Anonymous)
            {
                return true;
            }

            // Still allocating a enumerate from Identities -> Claims
            foreach (var claim in principal.Claims)
            {
                if (claim.Type == SecurityConstants.AuthLevelClaimType)
                {
                    var level = claim.Value switch
                    {
                        nameof(AuthorizationLevel.Admin) => AuthorizationLevel.Admin,
                        nameof(AuthorizationLevel.Function) => AuthorizationLevel.Function,
                        nameof(AuthorizationLevel.System) => AuthorizationLevel.System,
                        nameof(AuthorizationLevel.User) => AuthorizationLevel.User,
                        _ => AuthorizationLevel.Anonymous
                    };
                    if (level == AuthorizationLevel.Admin)
                    {
                        // If we have a claim with Admin level, regardless of whether a name is required, return true.
                        return true;
                    }
                    if (level == requiredLevel && (keyName == null || string.Equals(principal.FindFirstValue(SecurityConstants.AuthLevelKeyNameClaimType), keyName, StringComparison.OrdinalIgnoreCase)))
                    {
                        return true;
                    }
                }
            }

            return false;
        }

        public static bool PrincipalHasAuthLevelClaimHasClaim(ClaimsPrincipal principal, AuthorizationLevel requiredLevel, string keyName = null)
        {
            // If the required auth level is anonymous, the requirement is met
            if (requiredLevel == AuthorizationLevel.Anonymous)
            {
                return true;
            }

            var levelString = requiredLevel switch
            {
                AuthorizationLevel.Admin => nameof(AuthorizationLevel.Admin),
                AuthorizationLevel.Function => nameof(AuthorizationLevel.Function),
                AuthorizationLevel.System => nameof(AuthorizationLevel.System),
                AuthorizationLevel.User => nameof(AuthorizationLevel.User),
                _ => throw new ArgumentOutOfRangeException(nameof(requiredLevel))
            };

            return principal.HasClaim(c => c.Type == SecurityConstants.AuthLevelClaimType
                        && (c.Value == nameof(AuthorizationLevel.Admin)
                            || (c.Value == levelString && (keyName == null || string.Equals(principal.FindFirstValue(SecurityConstants.AuthLevelKeyNameClaimType), keyName, StringComparison.OrdinalIgnoreCase)))
                           ));
        }
    }
}
