// <copyright file="Settings.cs" company="AAllard">License: http://www.gnu.org/licenses/gpl.html GPL version 3.</copyright>

namespace FileConverter
{
    using System.Linq;
    using System;
    using System.Collections.Generic;
    using System.Xml.Serialization;
    using System.Collections.ObjectModel;
    using System.Globalization;

    using CommunityToolkit.Mvvm.ComponentModel;

    [XmlRoot]
    [XmlType]
    public class Settings : ObservableObject, IXmlSerializable
    {
        public const int Version = 4;

        private static readonly CultureInfo SupportedCulture = CultureInfo.GetCultureInfo("zh-CN");

        private bool exitApplicationWhenConversionsFinished = false;
        private float durationBetweenEndOfConversionsAndApplicationExit = 3f;
        private ObservableCollection<ConversionPreset> conversionPresets = new ObservableCollection<ConversionPreset>();
        private bool checkUpgradeAtStartup = true;
        private int maximumNumberOfSimultaneousConversions;
        private bool copyFilesInClipboardAfterConversion = false;
        private Helpers.HardwareAccelerationMode hardwareAccelerationMode = Helpers.HardwareAccelerationMode.Off;

        public ConversionPreset GetPresetFromName(string presetName)
        {
            return this.conversionPresets.FirstOrDefault(match => match.FullName == presetName);
        }

        public void Clean()
        {
            for (int index = 0; index < this.ConversionPresets.Count; index++)
            {
                this.ConversionPresets[index].Clean();
            }
        }

        public Settings Merge(Settings settings)
        {
            if (settings == null || settings.conversionPresets == null)
            {
                return this;
            }

            HashSet<string> presetNames = new HashSet<string>(StringComparer.Ordinal);
            foreach (ConversionPreset existingPreset in this.conversionPresets)
            {
                presetNames.Add(existingPreset.FullName);
            }

            for (int index = 0; index < settings.conversionPresets.Count; index++)
            {
                ConversionPreset conversionPreset = settings.conversionPresets[index];
                if (!presetNames.Add(conversionPreset.FullName))
                {
                    continue;
                }

                this.conversionPresets.Add(conversionPreset);
            }

            return this;
        }

        [XmlAttribute]
        public int SerializationVersion
        {
            get;
            set;
        } = Version;

        [XmlIgnore]
        public CultureInfo ApplicationLanguage
        {
            get => SupportedCulture;

            set
            {
                // 保留旧设置字段，但所有语言值均归一为简体中文。
                System.Threading.Thread.CurrentThread.CurrentCulture = SupportedCulture;
                System.Threading.Thread.CurrentThread.CurrentUICulture = SupportedCulture;
            }
        }

        [XmlElement]
        public string ApplicationLanguageName
        {
            get => SupportedCulture.Name;

            set
            {
                this.ApplicationLanguage = SupportedCulture;
            }
        }

        [XmlIgnore]
        public ObservableCollection<ConversionPreset> ConversionPresets
        {
            get => this.conversionPresets;

            set
            {
                this.conversionPresets = value;
                this.OnPropertyChanged();
            }
        }

        [XmlElement]
        public bool ExitApplicationWhenConversionsFinished
        {
            get => this.exitApplicationWhenConversionsFinished;

            set
            {
                this.exitApplicationWhenConversionsFinished = value;
                this.OnPropertyChanged();
            }
        }

        [XmlElement]
        public float DurationBetweenEndOfConversionsAndApplicationExit
        {
            get => this.durationBetweenEndOfConversionsAndApplicationExit;

            set
            {
                this.durationBetweenEndOfConversionsAndApplicationExit = value;
                this.OnPropertyChanged();
            }
        }

        [XmlElement]
        public int MaximumNumberOfSimultaneousConversions
        {
            get => this.maximumNumberOfSimultaneousConversions;

            set
            {
                this.maximumNumberOfSimultaneousConversions = value;
                this.OnPropertyChanged();
            }
        }

        [XmlElement("ConversionPreset")]
        public ConversionPreset[] SerializableConversionPresets
        {
            get => this.ConversionPresets.ToArray();

            set
            {
                for (int index = 0; index < value.Length; index++)
                {
                    this.ConversionPresets.Add(value[index]);
                }
            }
        }

        [XmlElement]
        public bool CheckUpgradeAtStartup
        {
            get => this.checkUpgradeAtStartup;

            set
            {
                this.checkUpgradeAtStartup = value;
                this.OnPropertyChanged();
            }
        }

        [XmlElement]
        public bool CopyFilesInClipboardAfterConversion
        {
            get => this.copyFilesInClipboardAfterConversion;

            set
            {
                this.copyFilesInClipboardAfterConversion = value;
                this.OnPropertyChanged();
            }
        }

        [XmlElement]
        public Helpers.HardwareAccelerationMode HardwareAccelerationMode
        {
            get => this.hardwareAccelerationMode;

            set
            {
                this.hardwareAccelerationMode = value;
                this.OnPropertyChanged();
            }
        }
        public void OnDeserializationComplete()
        {
            this.DurationBetweenEndOfConversionsAndApplicationExit = System.Math.Max(0, System.Math.Min(10, this.DurationBetweenEndOfConversionsAndApplicationExit));

            for (int index = 0; index < this.ConversionPresets.Count; index++)
            {
                this.ConversionPresets[index].OnDeserializationComplete();
            }

            // 兼容未保存语言字段的旧配置，并应用当前线程文化。
            this.ApplicationLanguage = SupportedCulture;
        }
    }
}
