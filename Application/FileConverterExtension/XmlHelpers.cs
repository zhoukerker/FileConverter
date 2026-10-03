// <copyright file="XmlHelpers.cs" company="AAllard">License: http://www.gnu.org/licenses/gpl.html GPL version 3.</copyright>

namespace FileConverterExtension
{
    using System;
    using System.Collections.Generic;
    using System.IO;
    using System.Xml;
    using System.Xml.Serialization;

    public class XmlHelpers
    {
        public static void LoadFromFile<T>(string root, string path, out T deserializedObject)
        {
            if (string.IsNullOrEmpty(path))
            {
                throw new ArgumentNullException(nameof(path));
            }

            XmlSerializer serializer = SerializerCache<T>.Get(root);

            using (StreamReader reader = new StreamReader(path))
            {
                XmlReaderSettings xmlReaderSettings = new XmlReaderSettings
                {
                    IgnoreWhitespace = true,
                    IgnoreComments = true
                };

                using (XmlReader xmlReader = XmlReader.Create(reader, xmlReaderSettings))
                {
                    lock (serializer)
                    {
                        deserializedObject = (T)serializer.Deserialize(xmlReader);
                    }
                }
            }
        }

        public static void SaveToFile<T>(string root, string path, T objectToSerialize)
        {
            if (string.IsNullOrEmpty(path))
            {
                throw new ArgumentNullException(nameof(path));
            }

            if (objectToSerialize == null)
            {
                throw new ArgumentNullException(nameof(objectToSerialize));
            }

            XmlSerializer serializer = SerializerCache<T>.Get(root);

            using (StreamWriter writer = new StreamWriter(path))
            {
                XmlWriterSettings xmlWriterSettings = new XmlWriterSettings
                {
                    Indent = true,
                    IndentChars = "    "
                };

                using (XmlWriter xmlWriter = XmlWriter.Create(writer, xmlWriterSettings))
                {
                    lock (serializer)
                    {
                        serializer.Serialize(xmlWriter, objectToSerialize);
                    }
                }
            }
        }

        private static class SerializerCache<T>
        {
            private static readonly Dictionary<string, XmlSerializer> Serializers = new Dictionary<string, XmlSerializer>(StringComparer.Ordinal);

            public static XmlSerializer Get(string root)
            {
                string key = root ?? string.Empty;
                lock (Serializers)
                {
                    if (!Serializers.TryGetValue(key, out XmlSerializer serializer))
                    {
                        // 自定义根节点的序列化器需要生成程序集，按类型和根名复用。
                        serializer = new XmlSerializer(typeof(T), new XmlRootAttribute { ElementName = root });
                        Serializers.Add(key, serializer);
                    }

                    return serializer;
                }
            }
        }
    }
}
