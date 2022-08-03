// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System;
using System.Net.Http;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Authorization.Policy;
using Microsoft.AspNetCore.Http;
using Microsoft.Azure.WebJobs.Script.WebHost.Extensions;
using Microsoft.Azure.WebJobs.Script.WebHost.Management;
using Microsoft.Azure.WebJobs.Script.WebHost.Security.Authorization.Policies;
using Microsoft.Extensions.DependencyInjection;

namespace Microsoft.Azure.WebJobs.Script.WebHost.Middleware
{
    public class VirtualFileSystemMiddleware : IMiddleware
    {
        private readonly VirtualFileSystem _vfs;
        private static readonly PathString _pathRoot = new PathString("/admin/vfs");

        public VirtualFileSystemMiddleware(VirtualFileSystem vfs)
        {
            _vfs = vfs;
        }

        /// <summary>
        /// A request is a vfs request if it starts with /admin/zip or /admin/vfs
        /// </summary>
        /// <param name="context">Current HttpContext</param>
        /// <returns>IsVirtualFileSystemRequest</returns>
        public static bool IsVirtualFileSystemRequest(HttpContext context)
        {
            return context.Request.Path.StartsWithSegments(_pathRoot);
        }

        public async Task InvokeAsync(HttpContext context, RequestDelegate requestDelegate)
        {
            var authorized = await AuthenticateAndAuthorize(context);

            if (!authorized)
            {
                context.Response.StatusCode = StatusCodes.Status401Unauthorized;
            }
            else
            {
                await InternalInvokeAsync(context);
            }
        }

        private async Task InternalInvokeAsync(HttpContext context)
        {
            // choose the right instance to use.
            HttpResponseMessage response = null;
            try
            {
                switch (context.Request.Method.ToLowerInvariant())
                {
                    case "get":
                        response = await _vfs.GetItem(context.Request);
                        break;

                    case "put":
                        response = await _vfs.PutItem(context.Request);
                        break;

                    case "delete":
                        response = await _vfs.DeleteItem(context.Request);
                        break;

                    default:
                        // VFS only supports GET, PUT, and DELETE
                        response = new HttpResponseMessage(System.Net.HttpStatusCode.MethodNotAllowed);
                        break;
                }

                context.Response.StatusCode = (int)response.StatusCode;

                // write response headers
                context.Response.Headers.AddRange(response.Headers.ToCoreHeaders());

                // This is to handle NullContent which != null, but has ContentLength of null.
                if (response.Content != null && response.Content.Headers.ContentLength != null)
                {
                    // Exclude content length to let ASP.NET Core take care of setting that based on the stream size.
                    context.Response.Headers.AddRange(response.Content.Headers.ToCoreHeaders("Content-Length"));
                    await response.Content.CopyToAsync(context.Response.Body);
                }
                response.Dispose();
            }
            catch (Exception e)
            {
                if (response != null)
                {
                    response.Dispose();
                }

                await context.Response.WriteAsync(e.Message);
            }
        }

        private async Task<bool> AuthenticateAndAuthorize(HttpContext context)
        {
            var authorizationPolicyProvider = context.RequestServices.GetRequiredService<IAuthorizationPolicyProvider>();
            var policyEvaluator = context.RequestServices.GetRequiredService<IPolicyEvaluator>();

            if (!AuthorizationOptionsExtensions.CheckPlatformInternal(context, allowAppServiceInternal: false))
            {
                return false;
            }

            var policy = await authorizationPolicyProvider.GetPolicyAsync(PolicyNames.AdminAuthLevel);
            var authenticateResult = await policyEvaluator.AuthenticateAsync(policy, context);

            // For admin, resource is null.
            var authorizeResult = await policyEvaluator.AuthorizeAsync(policy, authenticateResult, context, resource: null);

            return authorizeResult.Succeeded;
        }
    }
}
