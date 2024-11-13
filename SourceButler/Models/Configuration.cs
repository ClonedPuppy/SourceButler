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
}