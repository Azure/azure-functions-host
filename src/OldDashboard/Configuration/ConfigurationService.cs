using System;
using System.ComponentModel;
using System.ComponentModel.DataAnnotations;
using System.Configuration;
using System.Diagnostics;
using System.Globalization;
using System.Linq;
using System.Web;
using System.Web.Configuration;

namespace Dashboard.Configuration
{
    public class ConfigurationService
    {
        private const string SettingPrefix = "";

        private AppConfiguration _current;
        public AppConfiguration Current
        {
            get { return _current ?? (_current = Resolve()); }
            set { _current = value; }
        }

        public virtual AppConfiguration Resolve()
        {
            // Iterate over the properties
            var instance = new AppConfiguration();
            foreach (var property in TypeDescriptor.GetProperties(instance).Cast<PropertyDescriptor>().Where(p => !p.IsReadOnly))
            {
                // Try to get a config setting value
                string baseName = String.IsNullOrEmpty(property.DisplayName) ? property.Name : property.DisplayName;
                string settingName = SettingPrefix + baseName;

                string value = ReadSetting(settingName);

                if (String.IsNullOrEmpty(value))
                {
                    var defaultValue = property.Attributes.OfType<DefaultValueAttribute>().FirstOrDefault();
                    if (defaultValue != null && defaultValue.Value != null)
                    {
                        if (defaultValue.Value.GetType() == property.PropertyType)
                        {
                            property.SetValue(instance, defaultValue.Value);
                            continue;
                        }
                        else
                        {
                            value = defaultValue.Value as string;
                        }
                    }
                }

                if (value != null)
                {
                    if (property.PropertyType.IsAssignableFrom(typeof(string)))
                    {
                        property.SetValue(instance, value);
                    }
                    else if (property.Converter != null && property.Converter.CanConvertFrom(typeof(string)))
                    {
                        // Convert the value
                        property.SetValue(instance, property.Converter.ConvertFromString(value));
                    }
                }
                else if (property.Attributes.OfType<RequiredAttribute>().Any())
                {
                    throw new ConfigurationErrorsException(String.Format(CultureInfo.InvariantCulture, "Missing required configuration setting: '{0}'", settingName));
                }
            }
            return instance;
        }

        public virtual string ReadSetting(string settingName)
        {
            string value;
            var cstr = GetConnectionString(settingName);
            if (cstr != null)
            {
                value = cstr.ConnectionString;
            }
            else
            {
                value = GetAppSetting(settingName);
            }

            string cloudValue = GetCloudSetting(settingName);
            return String.IsNullOrEmpty(cloudValue) ? value : cloudValue;
        }

        [DebuggerNonUserCode]
        public virtual string GetCloudSetting(string settingName)
        {
#if false
            string value = null;
            try
            {
                if (RoleEnvironment.IsAvailable)
                {
                    value = RoleEnvironment.GetConfigurationSettingValue(settingName);
                }
            }
            catch (Exception)
            {
                // Not in the role environment or config setting not found...
            }
            return value;
#else
            // ###
            return null;
#endif
        }

        public virtual string GetAppSetting(string settingName)
        {
            return WebConfigurationManager.AppSettings[settingName];
        }

        public virtual ConnectionStringSettings GetConnectionString(string settingName)
        {
            return WebConfigurationManager.ConnectionStrings[settingName];
        }

        protected virtual HttpRequestBase GetCurrentRequest()
        {
            return new HttpRequestWrapper(HttpContext.Current.Request);
        }
    }
}
