// <copyright file="SettingsService.Migration.cs" company="AAllard">License: http://www.gnu.org/licenses/gpl.html GPL version 3.</copyright>

namespace FileConverter.Services
{
    using System;
    using FileConverter.ConversionJobs;

    public partial class SettingsService
    {
        private void MigrateSettingsToCurrentVersion(Settings settings)
        {
            int settingsVersion = settings.SerializationVersion;

            // 迁移转换设置。
            if (settings.ConversionPresets != null)
            {
                foreach (ConversionPreset conversionPreset in settings.ConversionPresets)
                {
                    this.MigrateConversionPresetToCurrentVersion(conversionPreset, settingsVersion);
                }
            }
        }

        private void MigrateConversionPresetToCurrentVersion(ConversionPreset preset, int settingsVersion)
        {
            if (settingsVersion <= 2)
            {
                // 迁移视频编码速度设置。
                string videoEncodingSpeed = preset.GetSettingsValue(ConversionPreset.ConversionSettingKeys.VideoEncodingSpeed);
                if (videoEncodingSpeed != null)
                {
                    VideoEncodingSpeed encodingSpeed;
                    if (!Enum.TryParse(videoEncodingSpeed, out encodingSpeed))
                    {
                        switch (videoEncodingSpeed)
                        {
                            case "Ultra Fast":
                                preset.SetSettingsValue(ConversionPreset.ConversionSettingKeys.VideoEncodingSpeed, VideoEncodingSpeed.UltraFast.ToString());
                                break;

                            case "Super Fast":
                                preset.SetSettingsValue(ConversionPreset.ConversionSettingKeys.VideoEncodingSpeed, VideoEncodingSpeed.SuperFast.ToString());
                                break;

                            case "Very Fast":
                                preset.SetSettingsValue(ConversionPreset.ConversionSettingKeys.VideoEncodingSpeed, VideoEncodingSpeed.VeryFast.ToString());
                                break;

                            case "Very Slow":
                                preset.SetSettingsValue(ConversionPreset.ConversionSettingKeys.VideoEncodingSpeed, VideoEncodingSpeed.VerySlow.ToString());
                                break;
                        }
                    }
                }
            }

            if (settingsVersion <= 3)
            {
                // 尝试修复损坏的设置（GitHub 问题 #5）。
                string scale = preset.GetSettingsValue(ConversionPreset.ConversionSettingKeys.ImageScale);
                if (scale != null)
                {
                    preset.SetSettingsValue(ConversionPreset.ConversionSettingKeys.ImageScale, scale.Replace(',', '.'));
                }

                scale = preset.GetSettingsValue(ConversionPreset.ConversionSettingKeys.VideoScale);
                if (scale != null)
                {
                    preset.SetSettingsValue(ConversionPreset.ConversionSettingKeys.VideoScale, scale.Replace(',', '.'));
                }
            }
        }
    }
}
