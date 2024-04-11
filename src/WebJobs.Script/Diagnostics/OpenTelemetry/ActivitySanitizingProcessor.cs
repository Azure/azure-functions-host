// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System.Collections.Generic;
using System.Diagnostics;
using Microsoft.Azure.WebJobs.Logging;
using OpenTelemetry;

namespace Microsoft.Azure.WebJobs.Script.Diagnostics.OpenTelemetry
{
    internal class ActivitySanitizingProcessor : BaseProcessor<Activity>
    {
        private static readonly IReadOnlyCollection<string> TagsToSanitize = new HashSet<string> { ResourceSemanticConventions.QueryUrl, ResourceSemanticConventions.FullUrl };

        private ActivitySanitizingProcessor() { }

        public static ActivitySanitizingProcessor Instance { get; } = new ActivitySanitizingProcessor();

        public override void OnEnd(Activity data)
        {
            Sanitize(data);

            base.OnEnd(data);
        }

        private static void Sanitize(Activity data)
        {
            foreach (var t in TagsToSanitize)
            {
                if (data.GetTagItem(t) is string s and not null)
                {
                    var sanitizedValue = Sanitizer.Sanitize(s);
                    data.SetTag(t, sanitizedValue);
                }
            }
        }
    }
}
