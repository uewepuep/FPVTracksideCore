using System;
using System.IO;
using System.Xml;
using Tools;

namespace FfmpegMediaPlatform
{
    /// <summary>
    /// Utility class for easily toggling HLS on/off via configuration files.
    /// This provides a simple way to control HLS without modifying code.
    /// </summary>
    public static class HlsToggleUtility
    {
        private const string GeneralSettingsPath = "data/GeneralSettings.xml";
        
        /// <summary>
        /// Toggle HLS on/off by modifying the GeneralSettings.xml file.
        /// This is useful for runtime control without restarting the application.
        /// </summary>
        /// <param name="enable">True to enable HLS, false to disable</param>
        /// <returns>True if the operation was successful</returns>
        public static bool ToggleHlsInConfig(bool enable)
        {
            try
            {
                if (!File.Exists(GeneralSettingsPath))
                {
                    Tools.Logger.VideoLog.LogCall(null, $"GeneralSettings.xml not found at {GeneralSettingsPath}");
                    return false;
                }

                // Load the XML document
                XmlDocument doc = new XmlDocument();
                doc.Load(GeneralSettingsPath);

                // Find the HlsEnabled element
                XmlNode hlsNode = doc.SelectSingleNode("//HlsEnabled");
                if (hlsNode == null)
                {
                    // If HlsEnabled doesn't exist, create it
                    XmlNode generalSettingsNode = doc.SelectSingleNode("//GeneralSettings");
                    if (generalSettingsNode != null)
                    {
                        XmlElement hlsElement = doc.CreateElement("HlsEnabled");
                        hlsElement.InnerText = enable.ToString().ToLower();
                        generalSettingsNode.AppendChild(hlsElement);
                    }
                    else
                    {
                        Tools.Logger.VideoLog.LogCall(null, "Could not find GeneralSettings node in XML");
                        return false;
                    }
                }
                else
                {
                    // Update existing HlsEnabled element
                    hlsNode.InnerText = enable.ToString().ToLower();
                }

                // Save the modified XML
                doc.Save(GeneralSettingsPath);
                
                // Update the runtime configuration
                if (enable)
                {
                    HlsConfig.EnableHls();
                }
                else
                {
                    HlsConfig.DisableHls();
                }

                Tools.Logger.VideoLog.LogCall(null, $"HLS {((enable) ? "enabled" : "disabled")} in {GeneralSettingsPath}");
                Tools.Logger.VideoLog.LogCall(null, $"Runtime HLS status: {HlsConfig.GetStatus()}");
                
                return true;
            }
            catch (Exception ex)
            {
                Tools.Logger.VideoLog.LogException(null, ex);
                Tools.Logger.VideoLog.LogCall(null, $"Failed to toggle HLS in config: {ex.Message}");
                return false;
            }
        }

        /// <summary>
        /// Enable HLS by setting the configuration file and runtime flag.
        /// </summary>
        /// <returns>True if successful</returns>
        public static bool EnableHlsInConfig()
        {
            return ToggleHlsInConfig(true);
        }

        /// <summary>
        /// Disable HLS by setting the configuration file and runtime flag.
        /// </summary>
        /// <returns>True if successful</returns>
        public static bool DisableHlsInConfig()
        {
            return ToggleHlsInConfig(false);
        }

        /// <summary>
        /// Get the current HLS setting from the configuration file.
        /// </summary>
        /// <returns>True if HLS is enabled in config, false if disabled, null if not found</returns>
        public static bool? GetHlsConfigValue()
        {
            try
            {
                if (!File.Exists(GeneralSettingsPath))
                {
                    return null;
                }

                XmlDocument doc = new XmlDocument();
                doc.Load(GeneralSettingsPath);

                XmlNode hlsNode = doc.SelectSingleNode("//HlsEnabled");
                if (hlsNode != null && bool.TryParse(hlsNode.InnerText, out bool value))
                {
                    return value;
                }

                return null;
            }
            catch (Exception ex)
            {
                Tools.Logger.VideoLog.LogException(null, ex);
                return null;
            }
        }

        /// <summary>
        /// Check if the configuration file and runtime status match.
        /// </summary>
        /// <returns>True if they match, false if there's a mismatch</returns>
        public static bool IsConfigInSync()
        {
            bool? configValue = GetHlsConfigValue();
            if (configValue.HasValue)
            {
                bool runtimeValue = HlsConfig.HlsEnabled;
                return configValue.Value == runtimeValue;
            }
            return false;
        }

        /// <summary>
        /// Sync the runtime HLS status with the configuration file.
        /// </summary>
        /// <returns>True if successful</returns>
        public static bool SyncWithConfig()
        {
            bool? configValue = GetHlsConfigValue();
            if (configValue.HasValue)
            {
                if (configValue.Value)
                {
                    HlsConfig.EnableHls();
                }
                else
                {
                    HlsConfig.DisableHls();
                }
                
                Tools.Logger.VideoLog.LogCall(null, $"HLS runtime status synced with config: {HlsConfig.GetStatus()}");
                return true;
            }
            
            Tools.Logger.VideoLog.LogCall(null, "No HLS configuration found, using default (disabled)");
            HlsConfig.DisableHls();
            return false;
        }

        /// <summary>
        /// Get a summary of the current HLS configuration status.
        /// </summary>
        /// <returns>Human-readable status summary</returns>
        public static string GetStatusSummary()
        {
            bool? configValue = GetHlsConfigValue();
            bool runtimeValue = HlsConfig.HlsEnabled;
            bool inSync = IsConfigInSync();

            string summary = $"HLS Status Summary:\n";
            summary += $"  Config File: {(configValue.HasValue ? (configValue.Value ? "Enabled" : "Disabled") : "Not Found")}\n";
            summary += $"  Runtime: {HlsConfig.GetStatus()}\n";
            summary += $"  In Sync: {(inSync ? "Yes" : "No")}\n";
            
            if (configValue.HasValue && !inSync)
            {
                summary += $"  ⚠️  Config and runtime are out of sync!\n";
                summary += $"  Use SyncWithConfig() to fix this.\n";
            }

            return summary;
        }
    }
}
