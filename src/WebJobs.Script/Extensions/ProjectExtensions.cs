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
        public static void AddPackageReference(this XmlDocument project, string packageId, string version)
        {
            var projectElements = project?.SelectNodes("//*").OfType<XmlElement>();

            XmlElement existingPackageReference = projectElements
                .FirstOrDefault(item => item?.Name == PackageReferenceElementName && item?.Attributes[PackageReferenceIncludesElementName]?.Value == packageId);

            if (existingPackageReference != null)
            {
                // If the package with the same version is already present, move on...
                if (existingPackageReference.Attributes[PackageReferenceVersionElementName]?.Value == version)
                {
                    return;
                }

                existingPackageReference.ParentNode?.RemoveChild(existingPackageReference);
            }

            XmlElement group = GetUniformItemGroupOrNew(project, PackageReferenceElementName);

            group.AppendChild(project.CreatePackageReference(packageId, version));
        }

        public static void RemovePackageReference(this XmlDocument project, string packageId)
        {
            var projectElements = project.SelectNodes("//*").OfType<XmlElement>();

            XmlElement existingPackageReference = projectElements
                .FirstOrDefault(item => item?.Name == PackageReferenceElementName && item?.Attributes[PackageReferenceIncludesElementName]?.Value == packageId);

            if (existingPackageReference != null)
            {
                existingPackageReference.ParentNode?.RemoveChild(existingPackageReference);
            }
        }

        public static XmlElement GetUniformItemGroupOrNew(this XmlDocument project, string itemName)
        {
            var projectElements = project?.SelectNodes("//*").OfType<XmlElement>();

            XmlElement group = projectElements
                                .Where(i => itemName.Equals(i.Name, StringComparison.Ordinal))
                                .FirstOrDefault();

            return group ?? project.AddItemGroup();
        }

        public static XmlElement CreateTargetFramework(this XmlDocument doc, string innerText)
        {
            XmlElement element = doc?.CreateElement(string.Empty, ScriptConstants.TargetFrameworkElementName, string.Empty);
            element.InnerText = innerText;
            return element;
        }

        public static XmlElement CreateItemGroup(this XmlDocument doc)
        {
            XmlElement itemGroup = doc?.CreateElement(string.Empty, ItemGroupElementName, string.Empty);
            return itemGroup;
        }

        public static XmlElement CreatePackageReference(this XmlDocument doc, string id, string version)
        {
            XmlElement element = doc?.CreateElement(string.Empty, PackageReferenceElementName, string.Empty);
            element.SetAttribute(PackageReferenceIncludesElementName, id);
            element.SetAttribute(PackageReferenceVersionElementName, version);
            return element;
        }

        public static XmlElement AddItemGroup(this XmlDocument doc)
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
