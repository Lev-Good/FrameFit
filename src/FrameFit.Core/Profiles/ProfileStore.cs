using System.Text.Json;
using System.Text.Json.Serialization;

namespace FrameFit.Core.Profiles;

/// <summary>
/// טעינה ושמירה של קובץ ההגדרות. כלל בטיחות (D4): קובץ חסר או פגום אינו מפיל את המערכת —
/// מוחזרת ברירת מחדל כבויה ומדווח על כך.
/// </summary>
public sealed class ProfileStore
{
    private static readonly JsonSerializerOptions Options = new()
    {
        WriteIndented = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.Never
    };

    public ProfileStore(string filePath)
    {
        FilePath = filePath;
    }

    public string FilePath { get; }

    public static SettingsDocument CreateDefault() => new();

    /// <summary>טוען את ההגדרות. <paramref name="wasReset"/> = true אם הקובץ היה חסר או פגום.</summary>
    public SettingsDocument Load(out bool wasReset)
    {
        wasReset = false;

        try
        {
            if (!File.Exists(FilePath))
            {
                wasReset = true;
                return CreateDefault();
            }

            var json = File.ReadAllText(FilePath);
            var document = JsonSerializer.Deserialize<SettingsDocument>(json, Options);
            if (document is null || document.SchemaVersion > SettingsDocument.CurrentSchemaVersion)
            {
                wasReset = true;
                return CreateDefault();
            }

            return document;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException)
        {
            wasReset = true;
            return CreateDefault();
        }
    }

    public void Save(SettingsDocument document)
    {
        var directory = Path.GetDirectoryName(FilePath);
        if (!string.IsNullOrEmpty(directory))
        {
            Directory.CreateDirectory(directory);
        }

        var json = JsonSerializer.Serialize(document, Options);
        File.WriteAllText(FilePath, json);
    }
}
