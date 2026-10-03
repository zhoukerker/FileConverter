// <copyright file="PresetDisplayNames.cs" company="AAllard">License: http://www.gnu.org/licenses/gpl.html GPL version 3.</copyright>

namespace FileConverterExtension
{
    using System;

    /// <summary>翻译内置预设的显示名称，保留实际名称和自定义名称。</summary>
    public static class PresetDisplayNames
    {
        public static string GetName(string name, bool isDefault)
        {
            if (!isDefault || string.IsNullOrEmpty(name))
            {
                return name;
            }

            string[] parts = name.Split('/');
            for (int index = 0; index < parts.Length; index++)
            {
                parts[index] = TranslatePart(parts[index]);
            }

            return string.Join("/", parts);
        }

        private static string TranslatePart(string name)
        {
            if (name.StartsWith("To ", StringComparison.Ordinal))
            {
                string format = name.Substring(3);
                int qualifier = format.IndexOf(" (", StringComparison.Ordinal);
                string suffix = qualifier < 0 ? string.Empty : format.Substring(qualifier)
                    .Replace("(low quality)", "（低质量）").Replace("(paged)", "（逐页）")
                    .Replace("(25% slower)", "（减速 25%）").Replace("(25% faster)", "（加速 25%）")
                    .Replace("(pitched -1st)", "（降低 1 个半音）").Replace("(pitched +1st)", "（升高 1 个半音）");
                return "转换为 " + (qualifier < 0 ? format : format.Substring(0, qualifier)).ToUpperInvariant() + suffix;
            }

            if (name.StartsWith("Extract DVD to ", StringComparison.Ordinal))
            {
                return "提取 DVD 为 " + name.Substring(15).ToUpperInvariant();
            }

            if (name.StartsWith("Extract CDA to ", StringComparison.Ordinal))
            {
                return "提取 CD 音轨为 " + name.Substring(15).ToUpperInvariant();
            }

            if (name.StartsWith("Scale ", StringComparison.Ordinal))
            {
                return "缩放至 " + name.Substring(6);
            }

            switch (name)
            {
                case "Rotate left": return "向左旋转";
                case "Rotate right": return "向右旋转";
                case "Tempo - Pitch": return "速度与音调";
                default: return name;
            }
        }
    }
}
