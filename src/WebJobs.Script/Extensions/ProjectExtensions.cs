// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System;
using System.Linq;
using System.Xml;
using static Microsoft.Azure.WebJobs.Script.ScriptConstants;

namespace Microsoft.Azure.WebJobs.Script.BindingExtensions
{
    internal static class ProjectExtensions
    {
        public static void CreateProject(this XmlDocument document)
        {
            XmlElement project = document.CreateElement(string.Empty, ProjectElementName, string.Empty);
            XmlElement propertyGroup = document.CreatePropertyGroup();
            XmlElement itemGroup = document.CreateItemGroup();

            propertyGroup.AppendChild(document.CreateWarningAsErrors());
            project.SetAttribute(ExtensionsProjectSdkAttributeName, ExtensionsProjectSdkPackageId);
            project.AppendChild(propertyGroup);
            project.AppendChild(itemGroup);
            document.AppendChild(project);
        }

        public static void AddPackageReference(this XmlDocument project, string packageId, string version)
        {
            var projectElements = project?.SelectNodes("//*").OfType<XmlElement>();

            XmlElement existingPackageReference = projectElements
                .FirstOrDefault(item => item?.Name == PackageReferenceElementName && item?.Attributes[PackageReferenceIncludeElementName]?.Value == packageId);

            if (existingPackageReference != null)
            {
                // If the package with the same version is already present, move on...
                if (existingPackageReference.Attributes[PackageReferenceVersionElementName]?.Value == version)
                {
                    return;
                }

                existingPackageReference.ParentNode?.RemoveChild(existingPackageReference);
            }

            XmlElement group = GetUniformItemGroupOrNew(project, ItemGroupElementName);

            group.AppendChild(project.CreatePackageReference(packageId, version));
        }

        public static void RemovePackageReference(this XmlDocument project, string packageId)
        {
            var projectElements = project.SelectNodes("//*").OfType<XmlElement>();

            XmlElement existingPackageReference = projectElements
                .FirstOrDefault(item => item?.Name == PackageReferenceElementName && item?.Attributes[PackageReferenceIncludeElementName]?.Value == packageId);

            if (existingPackageReference != null)
            {
                existingPackageReference.ParentNode?.RemoveChild(existingPackageReference);
            }
        }

        public static void AddTargetFramework(this XmlDocument project, string innerText)
        {
            var projectElements = project?.SelectNodes("//*").OfType<XmlElement>();

            XmlElement existingPackageReference = projectElements
                .FirstOrDefault(item => item?.Name == TargetFrameworkElementName && item.InnerText == innerText);

            if (existingPackageReference != null)
            {
                return;
            }

            XmlElement group = GetUniformItemGroupOrNew(project, PropertyGroupElementName);

            group.AppendChild(project.CreateTargetFramework(innerText));
        }

        public static void RemoveTargetFramework(this XmlDocument project, string innerText)
        {
            var projectElements = project.SelectNodes("//*").OfType<XmlElement>();

            XmlElement existingPackageReference = projectElements
                .FirstOrDefault(item => item?.Name == TargetFrameworkElementName && item.InnerText == innerText);

            if (existingPackageReference != null)
            {
                existingPackageReference.ParentNode?.RemoveChild(existingPackageReference);
            }
        }

        private static XmlElement GetUniformItemGroupOrNew(this XmlDocument project, string itemName)
        {
            var projectElements = project?.SelectNodes("//*").OfType<XmlElement>();

            XmlElement group = projectElements
                                .Where(i => itemName.Equals(i.Name, StringComparison.Ordinal))
                                .FirstOrDefault();

            return group ?? project.AddGroup();
        }

        private static XmlElement CreatePropertyGroup(this XmlDocument doc)
        {
            XmlElement itemGroup = doc?.CreateElement(string.Empty, PropertyGroupElementName, string.Empty);
            return itemGroup;
        }

        private static XmlElement CreateItemGroup(this XmlDocument doc)
        {
            XmlElement itemGroup = doc?.CreateElement(string.Empty, ItemGroupElementName, string.Empty);
            return itemGroup;
        }

        private static XmlElement CreateTargetFramework(this XmlDocument doc, string innerText)
        {
            XmlElement element = doc?.CreateElement(string.Empty, ScriptConstants.TargetFrameworkElementName, string.Empty);
            element.InnerText = innerText;
            return element;
        }

        private static XmlElement CreatePackageReference(this XmlDocument doc, string id, string version)
        {
            XmlElement element = doc?.CreateElement(string.Empty, PackageReferenceElementName, string.Empty);
            element.SetAttribute(PackageReferenceIncludeElementName, id);
            element.SetAttribute(PackageReferenceVersionElementName, version);
            return element;
        }

        private static XmlElement CreateWarningAsErrors(this XmlDocument doc)
        {
            XmlElement element = doc?.CreateElement(string.Empty, WarningsAsErrorsElementName, string.Empty);
            return element;
        }

        private static XmlElement AddGroup(this XmlDocument doc)
        {
            XmlElement reference = doc?.FirstChild?.ChildNodes?.OfType<XmlElement>().FirstOrDefault();
            XmlElement propertyGroups = doc?.FirstChild?.ChildNodes?.OfType<XmlElement>()
                                            .Where(i => PropertyGroupElementName.Equals(i.Name,  StringComparison.Ordinal))
                                            .FirstOrDefault();

            if (reference == null)
            {
                foreach (XmlElement propertyGroup in propertyGroups)
                {
                    reference = propertyGroup;
                    break;
                }
            }

            XmlElement newItemGroup = doc.CreateItemGroup();

            if (reference == null)
            {
                doc.AppendChild(newItemGroup);
            }
            else
            {
                doc.InsertAfter(newItemGroup, reference);
            }

            return newItemGroup;
        }
    }
}
