// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

namespace Microsoft.Azure.WebJobs.Script.WebHost.Features;

/// <summary>
/// Captures a matched route template for middleware outside the MVC pipeline.
/// </summary>
/// <param name="Template">The matched template, without request parameter values.</param>
internal sealed record HttpRouteTemplateFeature(string Template);
