using System;
using System.IO;
using System.Xml.Serialization;
using Turzx.Desktop.Models;

namespace Turzx.Desktop.Services
{
    internal static class ThemeProjectStore
    {
        public static string DefaultPath
        {
            get
            {
                return Path.Combine(Environment.GetFolderPath(
                    Environment.SpecialFolder.ApplicationData),
                    "TurzxControl", "themes", "default.turzxtheme.xml");
            }
        }

        public static string PresetDirectory
        {
            get
            {
                string executableDirectory = Path.GetDirectoryName(
                    typeof(ThemeProjectStore).Assembly.Location);
                return Path.Combine(executableDirectory, "Theme");
            }
        }

        public static void Save(ThemeProject project, string path)
        {
            if (project == null) throw new ArgumentNullException("project");
            string directory = Path.GetDirectoryName(path);
            if (!string.IsNullOrEmpty(directory)) Directory.CreateDirectory(directory);
            ThemeProject storedProject = CreateStoredProject(project, path);
            XmlSerializer serializer = new XmlSerializer(typeof(ThemeProject));
            using (FileStream stream = File.Create(path))
            {
                serializer.Serialize(stream, storedProject);
            }
        }

        public static ThemeProject Load(string path)
        {
            XmlSerializer serializer = new XmlSerializer(typeof(ThemeProject));
            using (FileStream stream = File.OpenRead(path))
            {
                ThemeProject project = (ThemeProject)serializer.Deserialize(stream);
                if (!string.IsNullOrWhiteSpace(project.BackgroundPath) &&
                    !Path.IsPathRooted(project.BackgroundPath))
                {
                    string directory = Path.GetDirectoryName(path) ?? string.Empty;
                    project.BackgroundPath = Path.GetFullPath(Path.Combine(
                        directory, project.BackgroundPath));
                }
                if (project.Components != null)
                {
                    string directory = Path.GetDirectoryName(path) ?? string.Empty;
                    foreach (EditorComponent component in project.Components)
                    {
                        if (!string.IsNullOrWhiteSpace(component.MediaPath) &&
                            !Path.IsPathRooted(component.MediaPath))
                            component.MediaPath = Path.GetFullPath(Path.Combine(
                                directory, component.MediaPath));
                    }
                }
                return project;
            }
        }

        private static ThemeProject CreateStoredProject(ThemeProject project, string themePath)
        {
            ThemeProject stored = CloneProject(project);
            string themeDirectory = Path.GetDirectoryName(themePath) ?? string.Empty;
            string themeBaseName = Path.GetFileNameWithoutExtension(themePath);
            string assetFolderName = themeBaseName + ".assets";
            string assetDirectory = Path.Combine(themeDirectory, assetFolderName);
            bool fileBackground = string.Equals(project.BackgroundKind, "Photo",
                StringComparison.OrdinalIgnoreCase) ||
                string.Equals(project.BackgroundKind, "Video",
                StringComparison.OrdinalIgnoreCase);
            if (fileBackground)
            {
                stored.BackgroundPath = StoreAsset(project.BackgroundPath,
                    assetDirectory, assetFolderName, "background");
            }
            if (project.Components != null && stored.Components != null)
            {
                int count = Math.Min(project.Components.Count, stored.Components.Count);
                for (int index = 0; index < count; index++)
                {
                    EditorComponent source = project.Components[index];
                    if (source.Kind != EditorComponentKind.Image &&
                        source.Kind != EditorComponentKind.Video) continue;
                    stored.Components[index].MediaPath = StoreAsset(source.MediaPath,
                        assetDirectory, assetFolderName, "component-" + (index + 1));
                }
            }
            return stored;
        }

        private static string StoreAsset(string sourcePath, string assetDirectory,
            string assetFolderName, string baseName)
        {
            if (string.IsNullOrWhiteSpace(sourcePath) || !File.Exists(sourcePath))
                throw new FileNotFoundException("主题资源文件不存在。", sourcePath);
            Directory.CreateDirectory(assetDirectory);
            string assetFileName = baseName + Path.GetExtension(sourcePath);
            string targetPath = Path.Combine(assetDirectory, assetFileName);
            string sourceFullPath = Path.GetFullPath(sourcePath);
            string targetFullPath = Path.GetFullPath(targetPath);
            if (!string.Equals(sourceFullPath, targetFullPath,
                StringComparison.OrdinalIgnoreCase))
                File.Copy(sourceFullPath, targetFullPath, true);
            return Path.Combine(assetFolderName, assetFileName);
        }

        private static ThemeProject CloneProject(ThemeProject project)
        {
            XmlSerializer serializer = new XmlSerializer(typeof(ThemeProject));
            using (MemoryStream stream = new MemoryStream())
            {
                serializer.Serialize(stream, project);
                stream.Position = 0;
                return (ThemeProject)serializer.Deserialize(stream);
            }
        }
    }
}
