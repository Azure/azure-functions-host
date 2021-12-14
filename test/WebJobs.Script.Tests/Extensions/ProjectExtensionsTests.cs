// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System;
using System.Linq;
using System.Xml.Linq;
using Microsoft.Azure.WebJobs.Script.BindingExtensions;
using Xunit;

namespace Microsoft.Azure.WebJobs.Script.Tests.Extensions
{
    public class ProjectExtensionsTests
    {
        private XDocument testDocument = new XDocument();

        public ProjectExtensionsTests()
        {
            XElement project =
                new XElement("Project",
                    new XElement("PropertyGroup"),
                    new XElement("ItemGroup"));

            testDocument.AddFirst(project);
        }

        public void Dispose()
        {
            testDocument = new XDocument();
        }

        [Fact]
        public void AddPackageReference_ReferenceDoesntExist_AddsNewPackage()
        {
            // Arrange
            var version = "1.0.0";

            // Act
            testDocument.AddPackageReference("Microsoft.Azure.WebJobs.Extensions.Test", version);

            // Assert
            Assert.Equal(1, testDocument.Descendants("PackageReference").Count());
            Assert.Equal(version, testDocument.Descendants("PackageReference").FirstOrDefault().Attribute("Version").Value);
            Assert.Equal("ItemGroup", testDocument.Descendants("PackageReference").FirstOrDefault().Parent.Name);
        }

        [Fact]
        public void AddPackageReference_ReferenceExists_SameVersion_DoesNothing()
        {
            // Arrange
            var version = "1.0.0";
            var testElement = new XElement("PackageReference",
                                    new XAttribute("Include", "Microsoft.Azure.WebJobs.Extensions.Test"),
                                    new XAttribute("Version", version));
            testDocument.Root.Element("ItemGroup").Add(testElement);

            // Act
            testDocument.AddPackageReference("Microsoft.Azure.WebJobs.Extensions.Test", version);

            // Assert
            Assert.Equal(1, testDocument.Descendants("PackageReference").Count());
            Assert.Equal(version, testDocument.Descendants("PackageReference").FirstOrDefault().Attribute("Version").Value);
        }

        [Fact]
        public void AddPackageReference_ReferenceExists_DifferentVersion_RemovesOldReferenceAndAddsNew()
        {
            // Arrange
            var existingVersion = "1.0.0";
            var newVersion = "2.0.0";
            var testElement = new XElement("PackageReference",
                                    new XAttribute("Include", "Microsoft.Azure.WebJobs.Extensions.Test"),
                                    new XAttribute("Version", existingVersion));
            testDocument.Root.Element("ItemGroup").Add(testElement);

            // Act
            testDocument.AddPackageReference("Microsoft.Azure.WebJobs.Extensions.Test", newVersion);

            // Assert
            Assert.Equal(1, testDocument.Descendants("PackageReference").Count());
            Assert.Equal(newVersion, testDocument.Descendants("PackageReference").FirstOrDefault().Attribute("Version").Value);
        }

        [Fact]
        public void AddPackageReference_WithoutItemGroup_CreatesItemGroupAndPackageReference()
        {
            // Arrange
            var version = "1.0.0";
            testDocument.Root.Element("ItemGroup").Remove();

            // Act
            testDocument.AddPackageReference("Microsoft.Azure.WebJobs.Extensions.Test", version);

            // Assert
            Assert.Equal(1, testDocument.Descendants("ItemGroup").Count());
            Assert.Equal(1, testDocument.Descendants("PackageReference").Count());
            Assert.Equal(version, testDocument.Descendants("PackageReference").FirstOrDefault().Attribute("Version").Value);
        }

        [Fact]
        public void AddPackageReference_WithEmptyItemGroupAndNonUniformedItemGroup_CreatesItemGroupAndPackageReference()
        {
            // Arrange
            var version = "1.0.0";
            testDocument.Root.Add(new XElement("ItemGroup", new XElement("UnwantedItemGroupElement")));

            // Act
            testDocument.AddPackageReference("Microsoft.Azure.WebJobs.Extensions.Test", version);

            // Assert
            var uniformedItemGroup = testDocument.Descendants("ItemGroup").FirstOrDefault(g => g.Elements().All(i => i.Name.LocalName == "PackageReference"));
            Assert.NotNull(uniformedItemGroup);
            Assert.Equal(2, testDocument.Descendants("ItemGroup").Count());
            Assert.Equal(1, uniformedItemGroup.Elements().Count());
        }

        [Fact]
        public void AddPackageReference_WithOnlyNonUniformedItemGroup_CreatesItemGroupAndPackageReference()
        {
            // Arrange
            var version = "1.0.0";
            testDocument.Root.Element("ItemGroup").Remove(); // remove default ItemGroup created during test setup
            testDocument.Root.Add(new XElement("ItemGroup", new XElement("UnwantedItemGroupElement")));

            // Act
            testDocument.AddPackageReference("Microsoft.Azure.WebJobs.Extensions.Test", version);

            // Assert
            var uniformedItemGroup = testDocument.Descendants("ItemGroup").FirstOrDefault(g => g.Elements().All(i => i.Name.LocalName == "PackageReference"));
            Assert.NotNull(uniformedItemGroup);
            Assert.Equal(2, testDocument.Descendants("ItemGroup").Count());
            Assert.Equal(1, uniformedItemGroup.Elements().Count());
        }

        [Fact]
        public void RemovePackageReference_ReferenceDoesntExist_DoesNothing()
        {
            // Act
            testDocument.RemovePackageReference("Microsoft.Azure.WebJobs.Extensions.Test");

            // Assert
            Assert.Equal(0, testDocument.Descendants("PackageReference").Count());
        }

        [Fact]
        public void RemovePackageReference_ReferenceExists_RemovesTargetFramework()
        {
            // Arrange
            var testElement = new XElement("PackageReference",
                                    new XAttribute("Include", "Microsoft.Azure.WebJobs.Extensions.Test"),
                                    new XAttribute("Version", "1.0.0"));
            testDocument.Root.Element("ItemGroup").Add(testElement);

            // Act
            testDocument.RemovePackageReference("Microsoft.Azure.WebJobs.Extensions.Test");

            // Assert
            Assert.Equal(0, testDocument.Descendants("PackageReference").Count());
        }
    }
}