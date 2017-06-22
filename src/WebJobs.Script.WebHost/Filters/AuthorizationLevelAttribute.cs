// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

#if HTTP_BINDING
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
using Microsoft.Azure.WebJobs.Extensions.Http;
using Microsoft.Azure.WebJobs.Script.WebHost.Security;

namespace Microsoft.Azure.WebJobs.Script.WebHost.Filters
{
    [AttributeUsage(AttributeTargets.Class | AttributeTargets.Method, Inherited = true, AllowMultiple = true)]
    public class AuthorizationLevelAttribute : ActionFilterAttribute, IAsyncAuthorizationFilter
    {
        public const string FunctionsKeyHeaderName = "x-functions-key";
        private readonly ISecretManager _secretManager;

        public AuthorizationLevelAttribute(AuthorizationLevel level, ISecretManager secretManager)
        {
            Level = level;
            _secretManager = secretManager;
        }

        public AuthorizationLevelAttribute(AuthorizationLevel level, string keyName)
        {
            if (string.IsNullOrEmpty(keyName))
            {
                throw new ArgumentNullException(nameof(keyName));
            }

            Level = level;
            KeyName = keyName;
        }

        public AuthorizationLevel Level { get; }

        public string KeyName { get; }

        public async override Task OnAuthorizationAsync(HttpActionContext actionContext, CancellationToken cancellationToken)
        {
            // TODO: FACAVAL 
            //if (context == null)
            //{
            //    throw new ArgumentNullException(nameof(context));
            //}

            //AuthorizationLevel requestAuthorizationLevel = context.HttpContext.Request.GetAuthorizationLevel();

            // If the request has not yet been authenticated, authenticate it
            var request = actionContext.Request;
            if (requestAuthorizationLevel == AuthorizationLevel.Anonymous)
            {
                // determine the authorization level for the function and set it
                // as a request property
                var secretManager = actionContext.ControllerContext.Configuration.DependencyResolver.GetService<ISecretManager>();

                var result = await GetAuthorizationResultAsync(request, secretManager, EvaluateKeyMatch, KeyName);
                requestAuthorizationLevel = result.AuthorizationLevel;
                request.SetAuthorizationLevel(result.AuthorizationLevel);
                request.SetProperty(ScriptConstants.AzureFunctionsHttpRequestKeyNameKey, result.KeyName);
            }

            if (request.IsAuthDisabled() ||
                SkipAuthorization(actionContext) ||
                Level == AuthorizationLevel.Anonymous)
            {
                // no authorization required
                return;
            }

            if (!request.HasAuthorizationLevel(Level))
            {
                actionContext.Response = new HttpResponseMessage(HttpStatusCode.Unauthorized);
            }
        }

        protected virtual string EvaluateKeyMatch(IDictionary<string, string> secrets, string keyName, string keyValue) => GetKeyMatchOrNull(secrets, keyName, keyValue);

        internal static Task<KeyAuthorizationResult> GetAuthorizationResultAsync(HttpRequestMessage request, ISecretManager secretManager, string keyName = null, string functionName = null)
        {
            return GetAuthorizationResultAsync(request, secretManager, GetKeyMatchOrNull, keyName, functionName);
        }

        internal static async Task<KeyAuthorizationResult> GetAuthorizationResultAsync(HttpRequestMessage request, ISecretManager secretManager,
            Func<IDictionary<string, string>, string, string, string> matchEvaluator, string keyName = null, string functionName = null)
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
                    return new KeyAuthorizationResult(ScriptConstants.DefaultMasterKeyName, AuthorizationLevel.Admin);
                }

                string matchedKeyName = matchEvaluator(hostSecrets.SystemKeys, keyName, keyValue);
                if (matchedKeyName != null)
                {
                    return new KeyAuthorizationResult(matchedKeyName, AuthorizationLevel.System);
                }

                // see if the key specified matches the host function key
                matchedKeyName = matchEvaluator(hostSecrets.FunctionKeys, keyName, keyValue);
                if (matchedKeyName != null)
                {
                    return new KeyAuthorizationResult(matchedKeyName, AuthorizationLevel.Function);
                }

                // if there is a function specific key specified try to match against that
                if (functionName != null)
                {
                    var functionSecrets = await secretManager.GetFunctionSecretsAsync(functionName);
                    matchedKeyName = matchEvaluator(functionSecrets, keyName, keyValue);
                    if (matchedKeyName != null)
                    {
                        return new KeyAuthorizationResult(matchedKeyName, AuthorizationLevel.Function);
                    }
                }
            }

            return new KeyAuthorizationResult(null, AuthorizationLevel.Anonymous);
        }

        internal static string GetKeyMatchOrNull(IDictionary<string, string> secrets, string keyName, string keyValue)
        {
            if (secrets != null)
            {
                foreach (var pair in secrets)
                {
                    if (Key.SecretValueEquals(pair.Value, keyValue) &&
                        (keyName == null || string.Equals(pair.Key, keyName, StringComparison.OrdinalIgnoreCase)))
                    {
                        return pair.Key;
                    }
                }
            }
            return null;
        }

        internal static bool SkipAuthorization(HttpActionContext actionContext)
        {
            return actionContext.ActionDescriptor.GetCustomAttributes<AllowAnonymousAttribute>().Count > 0
                || actionContext.ControllerContext.ControllerDescriptor.GetCustomAttributes<AllowAnonymousAttribute>().Count > 0;
        }
    }
}
#endif