// <copyright file="PresetDefinition.cs" company="AAllard">License: http://www.gnu.org/licenses/gpl.html GPL version 3.</copyright>

namespace FileConverterExtension
{
    using System;
    using System.Xml.Serialization;

    [XmlRoot("ConversionPreset")]
    [XmlType("ConversionPreset")]
    public class PresetReference
    {
        private string fullName;

        private PresetReference()
        {
        }

        [XmlAttribute("Name")]
        public string FullName
        {
            get => this.fullName;
            set
            {
                this.fullName = value;

                if (!string.IsNullOrEmpty(this.fullName))
                {
                    int separatorIndex = this.fullName.LastIndexOf('/');
                    this.Name = separatorIndex < 0 ? this.fullName : this.fullName.Substring(separatorIndex + 1);
                    this.Folders = separatorIndex < 0 ? Array.Empty<string>() : this.fullName.Substring(0, separatorIndex).Split('/');
                }
            }
        }

        [XmlElement]
        public string[] InputTypes
        {
            get;
            set;
        }

        [XmlAttribute]
        [System.ComponentModel.DefaultValue(false)]
        public bool IsDefaultSettings { get; set; }

        [XmlIgnore]
        public string DisplayName => PresetDisplayNames.GetName(this.Name, this.IsDefaultSettings);

        [XmlIgnore]
        public string Name
        {
            get;
            private set;
        }

        [XmlIgnore]
        public string[] Folders
        {
            get;
            private set;
        }
    }
}
