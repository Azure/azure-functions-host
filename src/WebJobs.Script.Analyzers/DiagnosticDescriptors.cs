// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System;
using System.Collections.Generic;
using System.Text;
using Microsoft.CodeAnalysis;

namespace Microsoft.Azure.Functions.Analyzers
{
    internal class DiagnosticDescriptors
    {
        private static DiagnosticDescriptor Create(string id, string title, string messageFormat, string category, DiagnosticSeverity severity)
        {
            string helpLink = $"https://docs.microsoft.com/azure/azure-functions/errors-diagnostics/sdk-rules/{id}"; 
            return new DiagnosticDescriptor(id, title, messageFormat, category, severity, isEnabledByDefault: true, helpLinkUri: helpLink);
        }

        public static DiagnosticDescriptor AsyncVoidDiscouraged { get; }
            = Create(id: "AZF0001", title: "Avoid async void", 
                messageFormat: "Async void can lead to unexpected behavior. Return Task instead.",
                category: Constants.DiagnosticsCategories.Usage,
                severity: DiagnosticSeverity.Error);

        public static DiagnosticDescriptor AvoidNonStaticHttpClient { get; }
            = Create(id: "AZF0002", title: "Inefficient HttpClient usage",
                messageFormat: "Reuse HttpClient instances to avoid holding more connections than necessary. See helplink for more information.",
                category: Constants.DiagnosticsCategories.Reliability,
                severity: DiagnosticSeverity.Warning);
    }
}
