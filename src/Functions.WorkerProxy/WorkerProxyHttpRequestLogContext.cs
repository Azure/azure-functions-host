// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

namespace Microsoft.Azure.Functions.WorkerProxy;

internal sealed record WorkerProxyHttpRequestLogContext(
    string Method,
    string Path,
    string Destination,
    string InvocationId,
    string TraceParent,
    string RequestId)
{
    private const string InvocationIdHeaderName = "x-ms-invocation-id";
    private const string RequestIdHeaderName = "x-ms-request-id";
    private const string TraceParentHeaderName = "traceparent";
    private const string MissingValue = "<none>";

    public static WorkerProxyHttpRequestLogContext Create(HttpRequest request, string destination)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(destination);

        return new WorkerProxyHttpRequestLogContext(
            request.Method,
            BuildPath(request),
            destination,
            GetHeaderValue(request.Headers, InvocationIdHeaderName),
            GetHeaderValue(request.Headers, TraceParentHeaderName),
            GetHeaderValue(request.Headers, RequestIdHeaderName));
    }

    private static string BuildPath(HttpRequest request)
    {
        string pathBase = request.PathBase.HasValue ? request.PathBase.Value! : string.Empty;
        string path = request.Path.HasValue ? request.Path.Value! : string.Empty;
        string query = request.QueryString.HasValue ? request.QueryString.Value! : string.Empty;
        return string.Concat(pathBase, path, query);
    }

    private static string GetHeaderValue(IHeaderDictionary headers, string headerName)
    {
        if (headers.TryGetValue(headerName, out var values))
        {
            string value = values.ToString();
            if (!string.IsNullOrWhiteSpace(value))
            {
                return value;
            }
        }

        return MissingValue;
    }
}
