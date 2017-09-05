// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System.Linq;
using Microsoft.Build.Construction;
using static Microsoft.Azure.WebJobs.Script.ScriptConstants;

namespace Microsoft.Azure.WebJobs.Script.BindingExtensions
{
    internal static class ProjectExtensions
    {
        public static void AddPackageReference(this ProjectRootElement project, string packageId, string version)
        {
            ProjectItemElement existingPackageReference = project.Items
                .FirstOrDefault(item => item.ItemType == PackageReferenceElementName && item.Include == packageId);

            if (existingPackageReference != null)
            {
                // If the package is already present, move on...
                if (existingPackageReference.Metadata.Any(m => m.Name == PackageReferenceVersionElementName && m.Value == version))
                {
                    return;
                }

                existingPackageReference.Parent.RemoveChild(existingPackageReference);
            }

            ProjectItemGroupElement group = GetUniformItemGroupOrNew(project, PackageReferenceElementName);

            group.AddItem(PackageReferenceElementName, packageId)
                 .AddMetadata(PackageReferenceVersionElementName, version, true);
        }

        public static void RemovePackageReference(this ProjectRootElement project, string packageId)
        {
            ProjectItemElement existingPackageReference = project.Items
                .FirstOrDefault(item => item.ItemType == PackageReferenceElementName && item.Include == packageId);

            if (existingPackageReference != null)
            {
                existingPackageReference.Parent.RemoveChild(existingPackageReference);
            }
        }

        public static ProjectItemGroupElement GetUniformItemGroupOrNew(this ProjectRootElement project, string itemName)
        {
            ProjectItemGroupElement group = project.ItemGroupsReversed.FirstOrDefault(g => g.Items.All(i => i.ItemType == itemName));

            return group ?? project.AddItemGroup();
        }
    }
}
