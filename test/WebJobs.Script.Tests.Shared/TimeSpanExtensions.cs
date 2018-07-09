// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System;
using System.Collections.Generic;
using System.Text;

namespace Microsoft.WebJobs.Script.Tests
{
    public static class TimeSpanExtensions
    {
        public static TimeSpan RoundSeconds(this TimeSpan timeSpan, int digits, MidpointRounding rounding = MidpointRounding.ToEven)
        {
            return TimeSpan.FromSeconds(Math.Round(timeSpan.TotalSeconds, digits, rounding));
        }
    }
}
