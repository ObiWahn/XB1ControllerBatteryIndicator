using System;
using System.ComponentModel;
using System.Configuration;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Xml.Linq;
using XB1ControllerBatteryIndicator.Properties;

namespace XB1ControllerBatteryIndicator
{
    //user.config lives in %LOCALAPPDATA%\<company>\<exe name>_Url_<hash>\<version>\. The hash depends on the exe path
    //and differs between .NET Framework and .NET, so a new version, runtime or install location starts with default
    //settings. On the first run of a build, import the most recently saved user.config of any build instead.
    //Based on bergi9/XB1ControllerBatteryIndicator@4de5a67.
    internal static class SettingsMigration
    {
        private const string SettingsSectionName = "XB1ControllerBatteryIndicator.Properties.Settings";

        public static void Run()
        {
            var settings = Settings.Default;
            if (!settings.UpgradeRequired)
            {
                return;
            }

            try
            {
                var currentConfigPath = ConfigurationManager.OpenExeConfiguration(ConfigurationUserLevel.PerUserRoamingAndLocal).FilePath;
                var previousConfigPath = FindMostRecentUserConfig(currentConfigPath);
                if (previousConfigPath != null)
                {
                    AppLogger.Info($"Importing settings from {WithoutUserProfile(previousConfigPath)}.");
                    Import(settings, previousConfigPath);
                }
            }
            catch (Exception ex)
            {
                AppLogger.Error("Importing settings failed.", ex);
            }

            settings.UpgradeRequired = false;
            settings.Save();
        }

        //the log may be attached to a bug report, so it shouldn't contain the user name from the profile path
        private static string WithoutUserProfile(string path)
        {
            var localAppData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
            return path.StartsWith(localAppData, StringComparison.OrdinalIgnoreCase)
                ? "%LOCALAPPDATA%" + path.Substring(localAppData.Length)
                : path;
        }

        //returns null if there is none or the current build's user.config is already the most recent one
        private static string FindMostRecentUserConfig(string currentConfigPath)
        {
            //<company>\<exe name>_<evidence>_<hash>\<version>\user.config
            var appDirectory = Path.GetDirectoryName(Path.GetDirectoryName(currentConfigPath));
            var companyDirectory = Path.GetDirectoryName(appDirectory);
            if (companyDirectory == null || !Directory.Exists(companyDirectory))
            {
                return null;
            }

            //the (truncated) exe name is shared by all builds, e.g. "XB1ControllerBatteryIndic"
            var exeNamePrefix = Path.GetFileName(appDirectory).Split('_')[0];
            var mostRecent = Directory.EnumerateDirectories(companyDirectory, exeNamePrefix + "_*")
                .SelectMany(directory => Directory.EnumerateFiles(directory, "user.config", SearchOption.AllDirectories))
                .Select(path => new FileInfo(path))
                .OrderByDescending(file => file.LastWriteTimeUtc)
                .FirstOrDefault();

            if (mostRecent == null ||
                string.Equals(mostRecent.FullName, Path.GetFullPath(currentConfigPath), StringComparison.OrdinalIgnoreCase))
            {
                return null;
            }
            return mostRecent.FullName;
        }

        //only settings this build knows are taken over, values that don't convert to the setting's type are skipped
        private static void Import(Settings settings, string configPath)
        {
            var section = XDocument.Load(configPath).Root?.Element("userSettings")?.Element(SettingsSectionName);
            if (section == null)
            {
                return;
            }

            foreach (var setting in section.Elements("setting"))
            {
                var name = (string)setting.Attribute("name");
                if (name == null || name == nameof(Settings.UpgradeRequired))
                {
                    continue;
                }
                var property = settings.Properties[name];
                if (property == null || (string)setting.Attribute("serializeAs") != "String")
                {
                    continue;
                }

                try
                {
                    settings[name] = TypeDescriptor.GetConverter(property.PropertyType)
                        .ConvertFromInvariantString((string)setting.Element("value") ?? "");
                }
                catch (Exception ex)
                {
                    Debug.WriteLine(ex);
                }
            }
        }
    }
}
