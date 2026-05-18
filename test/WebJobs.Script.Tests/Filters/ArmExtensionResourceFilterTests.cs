// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System;
using System.Collections.Generic;
using Microsoft.AspNetCore.Mvc.Controllers;
using Microsoft.Azure.WebJobs.Script.WebHost;
using Microsoft.Azure.WebJobs.Script.WebHost.Controllers;
using Microsoft.Azure.WebJobs.Script.WebHost.Filters;
using Microsoft.WebJobs.Script.Tests;
using Moq;
using Xunit;

namespace Microsoft.Azure.WebJobs.Script.Tests.Filters
{
    [Trait(TestTraits.Group, TestTraits.WebhookTests)]
    public class ArmExtensionResourceFilterTests
    {
        private const string Extension1 = "extension1";
        private const string Extension2 = "extension2";

        // ----- IsExtensionWebHookAction -----

        [Fact]
        public void IsExtensionWebHookAction_NullDescriptor_ReturnsFalse()
        {
            Assert.False(ArmExtensionResourceFilter.IsExtensionWebHookAction(null));
        }

        [Fact]
        public void IsExtensionWebHookAction_DescriptorWithNullMethodInfo_ReturnsFalse()
        {
            ControllerActionDescriptor descriptor = new() { MethodInfo = null };

            Assert.False(ArmExtensionResourceFilter.IsExtensionWebHookAction(descriptor));
        }

        [Fact]
        public void IsExtensionWebHookAction_DifferentControllerMethod_ReturnsFalse()
        {
            ControllerActionDescriptor descriptor = new()
            {
                MethodInfo = typeof(SomeOtherController).GetMethod(nameof(SomeOtherController.SomeMethod))
            };

            Assert.False(ArmExtensionResourceFilter.IsExtensionWebHookAction(descriptor));
        }

        [Fact]
        public void IsExtensionWebHookAction_HostControllerOtherMethod_ReturnsFalse()
        {
            ControllerActionDescriptor descriptor = new()
            {
                MethodInfo = typeof(HostController).GetMethod(nameof(HostController.Ping))
            };

            Assert.False(ArmExtensionResourceFilter.IsExtensionWebHookAction(descriptor));
        }

        [Fact]
        public void IsExtensionWebHookAction_ExtensionWebHookHandler_ReturnsTrue()
        {
            ControllerActionDescriptor descriptor = new()
            {
                MethodInfo = typeof(HostController).GetMethod(nameof(HostController.ExtensionWebHookHandler))
            };

            Assert.True(ArmExtensionResourceFilter.IsExtensionWebHookAction(descriptor));
        }

        // ----- IsArmWebhookOptInEnforced -----

        [Theory]
        [InlineData(null)]
        [InlineData("")]
        public void IsArmWebhookOptInEnforced_NullOrEmpty_ReturnsFalse(string rawConfig)
        {
            bool result = ArmExtensionResourceFilter.IsArmWebhookOptInEnforced(rawConfig, out IReadOnlySet<string> exclusions);

            Assert.False(result);
            Assert.Null(exclusions);
        }

        [Theory]
        [InlineData("|")]
        [InlineData("||")]
        [InlineData(" | ")]
        public void IsArmWebhookOptInEnforced_DelimiterOnly_ReturnsTrueWithNullExclusions(string rawConfig)
        {
            bool result = ArmExtensionResourceFilter.IsArmWebhookOptInEnforced(rawConfig, out IReadOnlySet<string> exclusions);

            Assert.True(result);
            Assert.Null(exclusions);
        }

        [Fact]
        public void IsArmWebhookOptInEnforced_SingleName_ReturnsTrueWithOneEntry()
        {
            bool result = ArmExtensionResourceFilter.IsArmWebhookOptInEnforced(Extension1, out IReadOnlySet<string> exclusions);

            Assert.True(result);
            Assert.Single(exclusions);
            Assert.Contains(Extension1, exclusions);
        }

        [Fact]
        public void IsArmWebhookOptInEnforced_MultipleNames_ReturnsAllEntries()
        {
            bool result = ArmExtensionResourceFilter.IsArmWebhookOptInEnforced($"{Extension1}|{Extension2}|other", out IReadOnlySet<string> exclusions);

            Assert.True(result);
            Assert.Equal(3, exclusions.Count);
            Assert.Contains(Extension1, exclusions);
            Assert.Contains(Extension2, exclusions);
            Assert.Contains("other", exclusions);
        }

        [Fact]
        public void IsArmWebhookOptInEnforced_TrimsAndDropsEmptyTokens()
        {
            bool result = ArmExtensionResourceFilter.IsArmWebhookOptInEnforced($" {Extension1} | other |", out IReadOnlySet<string> exclusions);

            Assert.True(result);
            Assert.Equal(2, exclusions.Count);
            Assert.Contains(Extension1, exclusions);
            Assert.Contains("other", exclusions);
        }

        [Fact]
        public void IsArmWebhookOptInEnforced_LookupIsCaseInsensitive()
        {
            bool result = ArmExtensionResourceFilter.IsArmWebhookOptInEnforced(Extension1.ToUpperInvariant(), out IReadOnlySet<string> exclusions);

            Assert.True(result);
            Assert.Contains(Extension1.ToUpperInvariant(), exclusions);
            Assert.Contains(Extension1, exclusions);
        }

        // ----- IsArmAllowedForExtension -----

        [Theory]
        [InlineData(null)]
        [InlineData("")]
        public void IsArmAllowedForExtension_NullOrEmptyName_ReturnsFalse(string extensionName)
        {
            IReadOnlySet<string> exclusions = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { Extension1 };
            Mock<IScriptWebHookProvider> provider = new(MockBehavior.Strict);

            bool result = ArmExtensionResourceFilter.IsArmAllowedForExtension(extensionName, exclusions, provider.Object);

            Assert.False(result);
            provider.Verify(p => p.IsArmAllowed(It.IsAny<string>()), Times.Never);
        }

        [Fact]
        public void IsArmAllowedForExtension_NameInExclusions_ReturnsTrueWithoutCallingProvider()
        {
            IReadOnlySet<string> exclusions = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { Extension1 };
            Mock<IScriptWebHookProvider> provider = new(MockBehavior.Strict);

            bool result = ArmExtensionResourceFilter.IsArmAllowedForExtension(Extension1, exclusions, provider.Object);

            Assert.True(result);
            provider.Verify(p => p.IsArmAllowed(It.IsAny<string>()), Times.Never);
        }

        [Fact]
        public void IsArmAllowedForExtension_NameInExclusions_LookupIsCaseInsensitive()
        {
            IReadOnlySet<string> exclusions = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { Extension1 };

            bool result = ArmExtensionResourceFilter.IsArmAllowedForExtension(Extension1.ToUpperInvariant(), exclusions, provider: null);

            Assert.True(result);
        }

        [Fact]
        public void IsArmAllowedForExtension_NotInExclusions_ProviderAllows_ReturnsTrue()
        {
            IReadOnlySet<string> exclusions = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            Mock<IScriptWebHookProvider> provider = new(MockBehavior.Strict);
            provider.Setup(p => p.IsArmAllowed("optedin")).Returns(true);

            bool result = ArmExtensionResourceFilter.IsArmAllowedForExtension("optedin", exclusions, provider.Object);

            Assert.True(result);
            provider.Verify(p => p.IsArmAllowed("optedin"), Times.Once);
        }

        [Fact]
        public void IsArmAllowedForExtension_NotInExclusions_ProviderDenies_ReturnsFalse()
        {
            IReadOnlySet<string> exclusions = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            Mock<IScriptWebHookProvider> provider = new(MockBehavior.Strict);
            provider.Setup(p => p.IsArmAllowed("optedout")).Returns(false);

            bool result = ArmExtensionResourceFilter.IsArmAllowedForExtension("optedout", exclusions, provider.Object);

            Assert.False(result);
            provider.Verify(p => p.IsArmAllowed("optedout"), Times.Once);
        }

        [Fact]
        public void IsArmAllowedForExtension_NotInExclusions_NullProvider_ReturnsFalse()
        {
            IReadOnlySet<string> exclusions = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            bool result = ArmExtensionResourceFilter.IsArmAllowedForExtension("optedout", exclusions, provider: null);

            Assert.False(result);
        }

        [Fact]
        public void IsArmAllowedForExtension_NullExclusions_ProviderConsulted()
        {
            // Defensive: even though the orchestration always supplies a non-null exclusions
            // set, the helper should not throw and should fall through to the provider.
            Mock<IScriptWebHookProvider> provider = new(MockBehavior.Strict);
            provider.Setup(p => p.IsArmAllowed("optedin")).Returns(true);

            bool result = ArmExtensionResourceFilter.IsArmAllowedForExtension("optedin", exclusions: null, provider.Object);

            Assert.True(result);
        }

        // Helper type used to verify IsExtensionWebHookAction rejects descriptors whose
        // declaring type is not HostController.
        private sealed class SomeOtherController
        {
            public void SomeMethod()
            {
            }
        }
    }
}
