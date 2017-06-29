// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

// This file is used by Code Analysis to maintain SuppressMessage 
// attributes that are applied to this project.
// Project-level suppressions either have no target or are given 
// a specific target and scoped to a namespace, type, member, etc.
//
// To add a suppression to this file, right-click the message in the 
// Code Analysis results, point to "Suppress Message", and click 
// "In Suppression File".
// You do not need to add suppressions to this file manually.

[assembly: System.Diagnostics.CodeAnalysis.SuppressMessage("Microsoft.Reliability", "CA2000:Dispose objects before losing scope", Scope = "member", Target = "Microsoft.Azure.WebJobs.Logging.ApplicationInsights.DefaultTelemetryClientFactory.#InitializeConfiguration()")]
[assembly: System.Diagnostics.CodeAnalysis.SuppressMessage("Microsoft.Usage", "CA2213:DisposableFieldsShouldBeDisposed", MessageId = "_perfModule", Scope = "member", Target = "Microsoft.Azure.WebJobs.Logging.ApplicationInsights.DefaultTelemetryClientFactory.#Dispose(System.Boolean)")]
[assembly: System.Diagnostics.CodeAnalysis.SuppressMessage("Microsoft.Usage", "CA2213:DisposableFieldsShouldBeDisposed", MessageId = "_quickPulseModule", Scope = "member", Target = "Microsoft.Azure.WebJobs.Logging.ApplicationInsights.DefaultTelemetryClientFactory.#Dispose(System.Boolean)")]
[assembly: System.Diagnostics.CodeAnalysis.SuppressMessage("Microsoft.Reliability", "CA2000:Dispose objects before losing scope", Scope = "member", Target = "Microsoft.Extensions.Logging.ApplicationInsightsLoggerExtensions.#AddApplicationInsights(Microsoft.Extensions.Logging.ILoggerFactory,Microsoft.Azure.WebJobs.Logging.ApplicationInsights.ITelemetryClientFactory)")]
[assembly: System.Diagnostics.CodeAnalysis.SuppressMessage("Microsoft.Reliability", "CA2000:Dispose objects before losing scope", Scope = "member", Target = "Microsoft.Extensions.Logging.ApplicationInsightsLoggerExtensions.#AddApplicationInsights(Microsoft.Extensions.Logging.ILoggerFactory,System.String,System.Func`3<System.String,Microsoft.Extensions.Logging.LogLevel,System.Boolean>)")]
[assembly: System.Diagnostics.CodeAnalysis.SuppressMessage("Microsoft.Design", "CA1014:MarkAssembliesWithClsCompliant")]