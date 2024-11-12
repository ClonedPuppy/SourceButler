// Configuration.cs - Add the constant
using SourceButler.ViewModels;
using System.IO;
using YamlDotNet.Serialization;

public class Configuration
{
    public List<string> SelectedFolders { get; set; } = new();
    public List<string> SelectedExtensions { get; set; } = new();
    public string LastRootDirectory { get; set; } = string.Empty;
    public const int MaxFileSize = 1024 * 1024; // 1MB
    public const string ConfigFileName = ".sourceButler.yml";

    public static Configuration LoadFromFile(string path)
    {
        if (!File.Exists(path)) return new Configuration();

        var deserializer = new DeserializerBuilder().Build();
        using var reader = new StreamReader(path);
        return deserializer.Deserialize<Configuration>(reader) ?? new Configuration();
    }

    public void SaveToFile(string path)
    {
        var serializer = new SerializerBuilder().Build();
        using var writer = new StreamWriter(path);
        serializer.Serialize(writer, this);
    }

    public class ValidationResult
    {
        public List<string> RemovedFolders { get; } = new();
        public List<string> RemovedExtensions { get; } = new();

        public bool HasChanges => RemovedFolders.Any() || RemovedExtensions.Any();

        public string GetSummary()
        {
            var summary = new List<string>();
            if (RemovedFolders.Any())
                summary.Add($"Removed {RemovedFolders.Count} invalid folder(s)");
            if (RemovedExtensions.Any())
                summary.Add($"Removed {RemovedExtensions.Count} invalid extension(s)");
            return string.Join(", ", summary);
        }
    }

    public ValidationResult Validate()
    {
        var result = new ValidationResult();

        // Validate folders
        for (int i = SelectedFolders.Count - 1; i >= 0; i--)
        {
            var folder = SelectedFolders[i];
            if (!Directory.Exists(folder) || IsExcludedFolder(folder))
            {
                result.RemovedFolders.Add(folder);
                SelectedFolders.RemoveAt(i);
            }
        }

        // Validate extensions (remove empty or invalid ones)
        for (int i = SelectedExtensions.Count - 1; i >= 0; i--)
        {
            var ext = SelectedExtensions[i];
            if (string.IsNullOrWhiteSpace(ext) || !ext.StartsWith("."))
            {
                result.RemovedExtensions.Add(ext);
                SelectedExtensions.RemoveAt(i);
            }
        }

        return result;
    }

    private bool IsExcludedFolder(string folderPath)
    {
        var folderName = Path.GetFileName(folderPath);
        return MainViewModel.ExcludedFolders.Contains(folderName);
    }
}