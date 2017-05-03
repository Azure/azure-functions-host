// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using Microsoft.Azure.WebJobs.Logging;
using Microsoft.Extensions.Logging;
using Xunit;

namespace Microsoft.Azure.WebJobs.Host.UnitTests.Loggers
{
    public class LogCategoryFilterTests
    {
        [Fact]
        public void Filter_MatchesLongestCategory()
        {
            var filter = new LogCategoryFilter();
            filter.DefaultLevel = LogLevel.Error;
            filter.CategoryLevels.Add("Microsoft", LogLevel.Critical);
            filter.CategoryLevels.Add("Microsoft.Azure", LogLevel.Error);
            filter.CategoryLevels.Add("Microsoft.Azure.WebJobs", LogLevel.Information);
            filter.CategoryLevels.Add("Microsoft.Azure.WebJobs.Host", LogLevel.Trace);

            Assert.False(filter.Filter("Microsoft", LogLevel.Information));
            Assert.False(filter.Filter("Microsoft.Azure", LogLevel.Information));
            Assert.False(filter.Filter("Microsoft.Azure.WebJob", LogLevel.Information));
            Assert.False(filter.Filter("NoMatch", LogLevel.Information));

            Assert.True(filter.Filter("Microsoft", LogLevel.Critical));
            Assert.True(filter.Filter("Microsoft.Azure", LogLevel.Critical));
            Assert.True(filter.Filter("Microsoft.Azure.WebJobs.Extensions", LogLevel.Information));
            Assert.True(filter.Filter("Microsoft.Azure.WebJobs.Host", LogLevel.Debug));
            Assert.True(filter.Filter("NoMatch", LogLevel.Error));
        }
    }
}
