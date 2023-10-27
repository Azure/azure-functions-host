﻿//------------------------------------------------------------------------------
// <auto-generated>
//     This code was generated by a tool.
//     Runtime Version:4.0.30319.42000
//
//     Changes to this file may cause incorrect behavior and will be lost if
//     the code is regenerated.
// </auto-generated>
//------------------------------------------------------------------------------

namespace Microsoft.Azure.WebJobs.Script.Properties {
    using System;
    
    
    /// <summary>
    ///   A strongly-typed resource class, for looking up localized strings, etc.
    /// </summary>
    // This class was auto-generated by the StronglyTypedResourceBuilder
    // class via a tool like ResGen or Visual Studio.
    // To add or remove a member, edit your .ResX file then rerun ResGen
    // with the /str option, or rebuild your VS project.
    [global::System.CodeDom.Compiler.GeneratedCodeAttribute("System.Resources.Tools.StronglyTypedResourceBuilder", "17.0.0.0")]
    [global::System.Diagnostics.DebuggerNonUserCodeAttribute()]
    [global::System.Runtime.CompilerServices.CompilerGeneratedAttribute()]
    internal class Resources {
        
        private static global::System.Resources.ResourceManager resourceMan;
        
        private static global::System.Globalization.CultureInfo resourceCulture;
        
        [global::System.Diagnostics.CodeAnalysis.SuppressMessageAttribute("Microsoft.Performance", "CA1811:AvoidUncalledPrivateCode")]
        internal Resources() {
        }
        
        /// <summary>
        ///   Returns the cached ResourceManager instance used by this class.
        /// </summary>
        [global::System.ComponentModel.EditorBrowsableAttribute(global::System.ComponentModel.EditorBrowsableState.Advanced)]
        internal static global::System.Resources.ResourceManager ResourceManager {
            get {
                if (object.ReferenceEquals(resourceMan, null)) {
                    global::System.Resources.ResourceManager temp = new global::System.Resources.ResourceManager("Microsoft.Azure.WebJobs.Script.Properties.Resources", typeof(Resources).Assembly);
                    resourceMan = temp;
                }
                return resourceMan;
            }
        }
        
        /// <summary>
        ///   Overrides the current thread's CurrentUICulture property for all
        ///   resource lookups using this strongly typed resource class.
        /// </summary>
        [global::System.ComponentModel.EditorBrowsableAttribute(global::System.ComponentModel.EditorBrowsableState.Advanced)]
        internal static global::System.Globalization.CultureInfo Culture {
            get {
                return resourceCulture;
            }
            set {
                resourceCulture = value;
            }
        }
        
        /// <summary>
        ///   Looks up a localized string similar to Your function must contain a single public method, a public method named &apos;Run&apos;, or a public method matching the name specified in the &apos;entryPoint&apos; metadata property..
        /// </summary>
        internal static string DotNetFunctionEntryPointRulesMessage {
            get {
                return ResourceManager.GetString("DotNetFunctionEntryPointRulesMessage", resourceCulture);
            }
        }
        
        /// <summary>
        ///   Looks up a localized string similar to Cannot delete an extension. Persistent file system not available in the current hosting environment.
        /// </summary>
        internal static string ErrorDeletingExtension {
            get {
                return ResourceManager.GetString("ErrorDeletingExtension", resourceCulture);
            }
        }
        
        /// <summary>
        ///   Looks up a localized string similar to Cannot install an extension. Persistent file system not available in the current hosting environment.
        /// </summary>
        internal static string ErrorInstallingExtension {
            get {
                return ResourceManager.GetString("ErrorInstallingExtension", resourceCulture);
            }
        }
        
        /// <summary>
        ///   Looks up a localized string similar to Cannot delete extension when ExtensionBundles is configured..
        /// </summary>
        internal static string ExtensionBundleBadRequestDelete {
            get {
                return ResourceManager.GetString("ExtensionBundleBadRequestDelete", resourceCulture);
            }
        }
        
        /// <summary>
        ///   Looks up a localized string similar to Cannot install extension when ExtensionBundles is configured..
        /// </summary>
        internal static string ExtensionBundleBadRequestInstall {
            get {
                return ResourceManager.GetString("ExtensionBundleBadRequestInstall", resourceCulture);
            }
        }
        
        /// <summary>
        ///   Looks up a localized string similar to Binding metadata not found within the extension bundle or extension bundle is not configured for the function app..
        /// </summary>
        internal static string ExtensionBundleBindingMetadataNotFound {
            get {
                return ResourceManager.GetString("ExtensionBundleBindingMetadataNotFound", resourceCulture);
            }
        }
        
        /// <summary>
        ///   Looks up a localized string similar to The value of id property in extensionBundle section of {0} file is invalid or missing. See https://aka.ms/functions-hostjson for more information.
        /// </summary>
        internal static string ExtensionBundleConfigMissingId {
            get {
                return ResourceManager.GetString("ExtensionBundleConfigMissingId", resourceCulture);
            }
        }
        
        /// <summary>
        ///   Looks up a localized string similar to The id and version property are missing in extensionBundle section of {0} file. See https://aka.ms/functions-hostjson for more information&quot;.
        /// </summary>
        internal static string ExtensionBundleConfigMissingMessage {
            get {
                return ResourceManager.GetString("ExtensionBundleConfigMissingMessage", resourceCulture);
            }
        }
        
        /// <summary>
        ///   Looks up a localized string similar to The value of version property in extensionBundle section of {0} file is invalid or missing. See https://aka.ms/functions-hostjson for more information.
        /// </summary>
        internal static string ExtensionBundleConfigMissingVersion {
            get {
                return ResourceManager.GetString("ExtensionBundleConfigMissingVersion", resourceCulture);
            }
        }
        
        /// <summary>
        ///   Looks up a localized string similar to Resources.{0} not found within the extension bundle or extension bundle is not configured for the function app..
        /// </summary>
        internal static string ExtensionBundleResourcesLocaleNotFound {
            get {
                return ResourceManager.GetString("ExtensionBundleResourcesLocaleNotFound", resourceCulture);
            }
        }
        
        /// <summary>
        ///   Looks up a localized string similar to Resources metadata not found within the extension bundle or extension bundle is not configured for the function app..
        /// </summary>
        internal static string ExtensionBundleResourcesNotFound {
            get {
                return ResourceManager.GetString("ExtensionBundleResourcesNotFound", resourceCulture);
            }
        }
        
        /// <summary>
        ///   Looks up a localized string similar to Templates not found within the extension bundle or extension bundle is not configured for the function app..
        /// </summary>
        internal static string ExtensionBundleTemplatesNotFound {
            get {
                return ResourceManager.GetString("ExtensionBundleTemplatesNotFound", resourceCulture);
            }
        }
        
        /// <summary>
        ///   Looks up a localized string similar to {{
        ///  Language: &quot;{0}&quot;,
        ///  FunctionName: &quot;{1}&quot;,
        ///  Success: {2}
        ///  IsStopwatchHighResolution: {3}
        ///}}.
        /// </summary>
        internal static string FunctionInvocationMetricsData {
            get {
                return ResourceManager.GetString("FunctionInvocationMetricsData", resourceCulture);
            }
        }
        
        /// <summary>
        ///   Looks up a localized string similar to A collision for Host ID &apos;{0}&apos; was detected in the configured storage account. For more information, see https://aka.ms/functions-hostid-collision..
        /// </summary>
        internal static string HostIdCollisionFormat {
            get {
                return ResourceManager.GetString("HostIdCollisionFormat", resourceCulture);
            }
        }
        
        /// <summary>
        ///   Looks up a localized string similar to WEBSITE_TIME_ZONE and TZ are not currently supported on the Linux Consumption plan. Please remove these environment variables..
        /// </summary>
        internal static string LinuxConsumptionRemoveTimeZone {
            get {
                return ResourceManager.GetString("LinuxConsumptionRemoveTimeZone", resourceCulture);
            }
        }
        
        /// <summary>
        ///   Looks up a localized string similar to Bundle version matching the {0} was not found.
        /// </summary>
        internal static string MatchingBundleNotFound {
            get {
                return ResourceManager.GetString("MatchingBundleNotFound", resourceCulture);
            }
        }
        
        /// <summary>
        ///   Looks up a localized string similar to SAS token within &apos;{0}&apos; setting has expired. Please generate a new SAS token or switch to using identites instead. For more information, see https://go.microsoft.com/fwlink/?linkid=2244092..
        /// </summary>
        internal static string SasTokenExpiredFormat {
            get {
                return ResourceManager.GetString("SasTokenExpiredFormat", resourceCulture);
            }
        }
        
        /// <summary>
        ///   Looks up a localized string similar to SAS token within &apos;{1}&apos; setting is set to expire in {0} days. Consider generating a new SAS token or switching to using identites instead. For more information, see https://go.microsoft.com/fwlink/?linkid=2244092..
        /// </summary>
        internal static string SasTokenExpiringFormat {
            get {
                return ResourceManager.GetString("SasTokenExpiringFormat", resourceCulture);
            }
        }
        
        /// <summary>
        ///   Looks up a localized string similar to SAS token within &apos;{1}&apos; setting is set to expire in {0} days..
        /// </summary>
        internal static string SasTokenExpiringInfoFormat {
            get {
                return ResourceManager.GetString("SasTokenExpiringInfoFormat", resourceCulture);
            }
        }
    }
}
