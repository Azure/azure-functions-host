// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using System.Web.Http;
using System.Web.Http.Controllers;
using System.Web.Http.Filters;

namespace Microsoft.Azure.WebJobs.Script.WebHost.Filters
{
    [AttributeUsage(AttributeTargets.Class | AttributeTargets.Method, Inherited = true, AllowMultiple = true)]
    public class AuthorizationLevelAttribute : AuthorizationFilterAttribute
    {
        public const string FunctionsKeyHeaderName = "x-functions-key";

        public AuthorizationLevelAttribute(AuthorizationLevel level)
        {
            Level = level;
        }

        public AuthorizationLevel Level { get; }

        public async override Task OnAuthorizationAsync(HttpActionContext actionContext, CancellationToken cancellationToken)
        {
            if (actionContext == null)
            {
                throw new ArgumentNullException("actionContext");
            }

            // determine the authorization level for the function and set it
            // as a request property
            var secretManager = actionContext.ControllerContext.Configuration.DependencyResolver.GetService<ISecretManager>();
            var settings = actionContext.ControllerContext.Configuration.DependencyResolver.GetService<WebHostSettings>();
            var requestAuthorizationLevel = await GetAuthorizationLevelAsync(actionContext.Request, secretManager, EvaluateKeyMatch);
            actionContext.Request.Properties[ScriptConstants.AzureFunctionsHttpRequestAuthorizationLevel] = requestAuthorizationLevel;

            if (settings.IsAuthDisabled || 
                SkipAuthorization(actionContext) ||
                Level == AuthorizationLevel.Anonymous)
            {
                return;
            }

            if (requestAuthorizationLevel < Level)
            {
                actionContext.Response = new HttpResponseMessage(HttpStatusCode.Unauthorized);
            }
        }

        protected virtual bool EvaluateKeyMatch(IDictionary<string, string> secrets, string keyValue) => HasMatchingKey(secrets, keyValue);

        internal static Task<AuthorizationLevel> GetAuthorizationLevelAsync(HttpRequestMessage request, ISecretManager secretManager, string functionName = null)
        {
            return GetAuthorizationLevelAsync(request, secretManager, HasMatchingKey, functionName);
        }

        internal static async Task<AuthorizationLevel> GetAuthorizationLevelAsync(HttpRequestMessage request, ISecretManager secretManager,
            Func<IDictionary<string, string>, string, bool> matchEvaluator, string functionName = null)
        {
            // first see if a key value is specified via headers or query string (header takes precedence)
            IEnumerable<string> values;
            string keyValue = null;
            if (request.Headers.TryGetValues(FunctionsKeyHeaderName, out values))
            {
                keyValue = values.FirstOrDefault();
            }
            else
            {
                var queryParameters = request.GetQueryParameterDictionary();
                queryParameters.TryGetValue("code", out keyValue);
            }

            if (!string.IsNullOrEmpty(keyValue))
            {
                // see if the key specified is the master key
                HostSecretsInfo hostSecrets = await secretManager.GetHostSecretsAsync().ConfigureAwait(false);
                if (!string.IsNullOrEmpty(hostSecrets.MasterKey) &&
                    Key.SecretValueEquals(keyValue, hostSecrets.MasterKey))
                {
                    return AuthorizationLevel.Admin;
                }

                if (matchEvaluator(hostSecrets.SystemKeys, keyValue))
                {
                    return AuthorizationLevel.System;
                }

                // see if the key specified matches the host function key
                if (matchEvaluator(hostSecrets.FunctionKeys, keyValue))
                {
                    return AuthorizationLevel.Function;
                }

                // if there is a function specific key specified try to match against that
                if (functionName != null)
                {
                    IDictionary<string, string> functionSecrets = await secretManager.GetFunctionSecretsAsync(functionName);
                    if (matchEvaluator(functionSecrets, keyValue))
                    {
                        return AuthorizationLevel.Function;
                    }
                }
            }

            return AuthorizationLevel.Anonymous;
        }

        private static bool HasMatchingKey(IDictionary<string, string> secrets, string keyValue) 
            => secrets != null && secrets.Values.Any(s => Key.SecretValueEquals(s, keyValue));

        internal static bool SkipAuthorization(HttpActionContext actionContext)
        {
            return actionContext.ActionDescriptor.GetCustomAttributes<AllowAnonymousAttribute>().Count > 0
                || actionContext.ControllerContext.ControllerDescriptor.GetCustomAttributes<AllowAnonymousAttribute>().Count > 0;
        }
    }
}