// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System;

namespace Microsoft.Azure.WebJobs.Logging.Internal
{
    /// <summary>
    /// Create projection from table rows to chartable data
    /// </summary>
    public static class ProjectionHelper
    {
        /// 
        public static Tuple<long, double>[] Work(InstanceCountEntity[] rows, long startTicks, long endTicks, int numberBuckets)
        {
            if (rows == null)
            {
                throw new ArgumentNullException("rows");
            }

            int N = numberBuckets;
            double bucketWidthTicks = ((double)(endTicks - startTicks)) / N;

            double[] values = new double[N];

            Action<int, double> add = (index, amount) =>
            {
                if (index >= 0 && index < values.Length)
                {
                    values[index] += amount;
                }
            };

            // need a projection from Rows to buckets. 
            // rows are are sparse array 
            // buckets are a dense linear array. 
            foreach (var entity in rows)
            {
                long ticks1 = entity.GetTicks();
                double idx1 = ((ticks1 - startTicks) / bucketWidthTicks);

                // Spread the area across all buckets. 
                long ticks2 = entity.GetEndTicks();
                double idx2 = ((ticks2 - startTicks) / bucketWidthTicks);

                double area = (entity.CurrentActive * entity.MachineSize) * entity.GetDurationInTicks();
                double area2 = area / bucketWidthTicks;

                // 3 sections 
                double pfull = idx2 - idx1;

                if (pfull < 1)
                {
                    // entire projection is smaller than a single bucket
                    double val = area2;
                    add((int)idx1, val);
                }
                else
                {
                    // projection spans multiple puckets. 
                    int idx1Whole = RoundUp(idx1);
                    int idx2Whole = RoundDown(idx2);

                    double pleft = idx1Whole - idx1;
                    double pright = idx2 - idx2Whole;

                    double val_mid = area2 / pfull;
                    double val_left = val_mid * pleft;
                    double val_right = val_mid * pright;

                    add((int)idx1, val_left);
                    for (int i = idx1Whole; i < idx2Whole; i++)
                    {
                        add(i, val_mid);
                    }
                    add((int)idx2, val_right);
                }
            }

            // Convert to output format. 
            Tuple<long, double>[] chart = new Tuple<long, double>[N];
            for (int i = 0; i < N; i++)
            {
                long ticks = (long)(startTicks + (i * bucketWidthTicks));
                chart[i] = new Tuple<long, double>(ticks, values[i]);
            }

            return chart;
        }

        static int RoundUp(double d)
        {
            int i = (int)d; // truncate
            if (i == d)
            {
                return i;
            }
            return i + 1;
        }
        static int RoundDown(double d)
        {
            return (int)d;
        }
    }
}
