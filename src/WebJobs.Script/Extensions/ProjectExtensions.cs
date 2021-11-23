// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System;
using System.Linq;
using System.Xml.Linq;
using static Microsoft.Azure.WebJobs.Script.ScriptConstants;

namespace Microsoft.Azure.WebJobs.Script.BindingExtensions
{
    internal static class ProjectExtensions
    {
        public static void AddPackageReference(this XDocument document, string packageId, string version)
        {
            XElement existingPackageReference = document.Descendants()?.FirstOrDefault(
                                                        item =>
                                                        PackageReferenceElementName.Equals(item.Name?.LocalName, StringComparison.Ordinal) &&
                                                        item.Attribute(PackageReferenceIncludeElementName)?.Value == packageId);

            if (existingPackageReference != null)
            {
                // If the package with the same version is already present, move on...
                if (existingPackageReference.Attribute(PackageReferenceVersionElementName)?.Value == version)
                {
                    return;
                }

                existingPackageReference.Remove();
            }

            XElement group = document.GetUniformItemGroupOrNew(PackageReferenceElementName);
            XElement element = new XElement(PackageReferenceElementName,
                                    new XAttribute(PackageReferenceIncludeElementName, packageId),
                                    new XAttribute(PackageReferenceVersionElementName, version));
            group.Add(element);
        }

        public static void RemovePackageReference(this XDocument document, string packageId)
        {
            XElement existingPackageReference = document.Descendants()?.FirstOrDefault(
                                                        item =>
                                                        PackageReferenceElementName.Equals(item.Name?.LocalName, StringComparison.Ordinal) &&
                                                        item.Attribute(PackageReferenceIncludeElementName)?.Value == packageId);
            if (existingPackageReference != null)
            {
                existingPackageReference.Remove();
            }
        }

        internal static XElement GetUniformItemGroupOrNew(this XDocument document, string itemName)
        {
            XElement group = document.Descendants(ItemGroupElementName)
                                        .LastOrDefault(g => g.Elements()
                                            .All(i => itemName.Equals(i.Name?.LocalName, StringComparison.Ordinal)));

            if (group == null)
            {
                document.Root.Add(new XElement(ItemGroupElementName));
                group = document.Descendants(ItemGroupElementName).LastOrDefault();
            }

            return group;
        }
    }
}
