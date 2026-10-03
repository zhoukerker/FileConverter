// <copyright file="XmlHelpers.cs" company="AAllard">License: http://www.gnu.org/licenses/gpl.html GPL version 3.</copyright>

namespace FileConverter
{
    public class XmlHelpers
    {
        public static void LoadFromFile<T>(string root, string path, out T deserializedObject)
        {
            FileConverterExtension.XmlHelpers.LoadFromFile(root, path, out deserializedObject);

            if (deserializedObject is IXmlSerializable xmlSerializableObject)
            {
                xmlSerializableObject.OnDeserializationComplete();
            }
        }
        
        public static void SaveToFile<T>(string root, string path, T objectToSerialize)
        {
            try
            {
                FileConverterExtension.XmlHelpers.SaveToFile(root, path, objectToSerialize);
            }
            catch (System.Exception exception)
            {
                Diagnostics.Debug.LogError("无法保存类型 '" + typeof(T) + "' 的数据，异常信息：\n " + exception.Message);
            }
        }
    }
}
