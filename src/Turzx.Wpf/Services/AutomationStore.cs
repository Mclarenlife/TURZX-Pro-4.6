using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Xml.Serialization;
using Turzx.Desktop.Models;

namespace Turzx.Desktop.Services
{
    [Serializable]
    public sealed class AutomationCollection
    {
        public AutomationCollection()
        {
            Rules = new List<AutomationRule>();
        }

        public List<AutomationRule> Rules { get; set; }
    }

    internal static class AutomationStore
    {
        public static string DefaultPath
        {
            get
            {
                return Path.Combine(Environment.GetFolderPath(
                    Environment.SpecialFolder.ApplicationData),
                    "TurzxControl", "automations.xml");
            }
        }

        public static ObservableCollection<AutomationRule> LoadOrDefault()
        {
            if (!File.Exists(DefaultPath))
                return new ObservableCollection<AutomationRule>();
            try
            {
                XmlSerializer serializer = new XmlSerializer(typeof(AutomationCollection));
                using (FileStream stream = File.OpenRead(DefaultPath))
                {
                    AutomationCollection stored =
                        (AutomationCollection)serializer.Deserialize(stream);
                    return new ObservableCollection<AutomationRule>(stored.Rules ??
                        new List<AutomationRule>());
                }
            }
            catch
            {
                return new ObservableCollection<AutomationRule>();
            }
        }

        public static void Save(IEnumerable<AutomationRule> rules)
        {
            string directory = Path.GetDirectoryName(DefaultPath);
            if (!string.IsNullOrEmpty(directory)) Directory.CreateDirectory(directory);
            AutomationCollection stored = new AutomationCollection();
            if (rules != null) stored.Rules.AddRange(rules);
            string temporaryPath = DefaultPath + ".tmp";
            XmlSerializer serializer = new XmlSerializer(typeof(AutomationCollection));
            using (FileStream stream = File.Create(temporaryPath))
                serializer.Serialize(stream, stored);
            File.Copy(temporaryPath, DefaultPath, true);
            File.Delete(temporaryPath);
        }
    }
}
