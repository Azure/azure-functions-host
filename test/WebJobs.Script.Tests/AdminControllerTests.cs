// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System.Reflection;
using Microsoft.Azure.WebJobs.Script;
using WebJobs.Script.WebHost;
using WebJobs.Script.WebHost.Controllers;
using WebJobs.Script.WebHost.Filters;
using Xunit;

namespace WebJobs.Script.Tests
{
    public class AdminControllerTests
    {
        [Fact]
        public void AdminController_HasAuthorizationLevelAttribute()
        {
            AuthorizationLevelAttribute attribute = typeof(AdminController).GetCustomAttribute<AuthorizationLevelAttribute>();
            Assert.Equal(AuthorizationLevel.Admin, attribute.Level);
        }
    }
}
