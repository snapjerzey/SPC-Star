using System.Text.Json;
using System.Text.Json.Serialization;

namespace SPCStar.Core.Infrastructure;

public sealed class FileBackedSpcRepository : InMemorySpcRepository, IRepositoryPersistence
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        Converters = { new JsonStringEnumConverter() }
    };

    public FileBackedSpcRepository(string storagePath)
    {
        StoragePath = storagePath;
        Load();
    }

    public string StoragePath { get; }

    public void SaveChanges()
    {
        var directory = Path.GetDirectoryName(StoragePath);
        if (!string.IsNullOrWhiteSpace(directory))
        {
            Directory.CreateDirectory(directory);
        }

        var snapshot = RepositorySnapshot.FromRepository(this);
        var json = JsonSerializer.Serialize(snapshot, JsonOptions);
        var tempPath = $"{StoragePath}.tmp";
        File.WriteAllText(tempPath, json);

        if (File.Exists(StoragePath))
        {
            File.Replace(tempPath, StoragePath, null);
            return;
        }

        File.Move(tempPath, StoragePath);
    }

    private void Load()
    {
        if (!File.Exists(StoragePath))
        {
            return;
        }

        var json = File.ReadAllText(StoragePath);
        if (string.IsNullOrWhiteSpace(json))
        {
            return;
        }

        var snapshot = JsonSerializer.Deserialize<RepositorySnapshot>(json, JsonOptions);
        snapshot?.CopyTo(this);
    }
}
