// Copyright (c) Microsoft Open Technologies, Inc. All rights reserved.
// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information.

using System;

namespace Microsoft.Azure.Jobs.Host.Timers
{
    internal static class RandomExtensions
    {
        public static double Next(this Random random, double minValue, double maxValue)
        {
            if (random == null)
            {
                throw new ArgumentNullException("random");
            }

            return (maxValue - minValue) * random.NextDouble() + minValue;
        }
    }
}
