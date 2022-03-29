using System;
using System.IO;
using System.Reflection;
using System.Xml;
using System.Xml.Serialization;


namespace DeZogPlugin
{
    public class Settings
    {
        /**
         *  The port that clients can connect to.
         */
        public int Port { get; set; } = 11000;  // Default

        /**
         * If log is enabled.
         */
        public bool LogEnabled { get; set; } = false;  // Default


        /**
         * Constructor.
         */
        public Settings()
        {
        }


        /**
         * Load the settings file.
         */
        public static Settings Load()
        {
            Settings settings;
            string fileName = GetFileName();
            try
            {
                string xml = File.ReadAllText(fileName);
                var reader = new StringReader(xml);
                using (reader)
                {
                    var serializer = new XmlSerializer(typeof(Settings));
                    settings = (Settings)serializer.Deserialize(reader);
                    reader.Close();
                }
            }
            catch (Exception /*exc*/)
            {
                // Maybe file does not exist.
                settings = new Settings();
            }

            return settings;
        }


        /**
         * Returns the settings filename.
         * I.e. DeZogpPlugin.dll.config
         */
        protected static string GetFileName()
        {
            string path = AppDomain.CurrentDomain.BaseDirectory;
            var dll = Assembly.GetAssembly(typeof(Settings)).ManifestModule.ScopeName + ".config";
            return Path.Combine(path, dll);
        }
    }
}
