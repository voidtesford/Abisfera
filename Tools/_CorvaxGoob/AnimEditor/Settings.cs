using System.Text.Json;
using Abisfera.AnimEditor.Rendering;

namespace Abisfera.AnimEditor;

/// <summary>Remembered between launches (%APPDATA%/AbisferaAnimEditor/settings.json).</summary>
public sealed class Settings
{
    public string? Resources { get; set; }
    public string? LastScene { get; set; }
    public string Mode { get; set; } = "Finisher";
    public List<Appearance> Puppets { get; set; } = new() { Appearance.Default(0), Appearance.Default(1) };
    public bool Onion { get; set; } = true;
    public bool Bones { get; set; } = true;
    public bool Limits { get; set; } = true;
    public bool Effects { get; set; } = true;
    public bool Procedural { get; set; } = true;
    public bool Loop { get; set; } = true;
    public bool AutoKey { get; set; } = true;
    public double Width { get; set; } = 1500;
    public double Height { get; set; } = 920;

    public static string Dir => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "AbisferaAnimEditor");
    public static string FilePath => Path.Combine(Dir, "settings.json");

    private static readonly JsonSerializerOptions Options = new() { WriteIndented = true };

    public static Settings Load()
    {
        try
        {
            if (File.Exists(FilePath))
                return JsonSerializer.Deserialize<Settings>(File.ReadAllText(FilePath), Options) ?? new Settings();
        }
        catch
        {
            // a broken file: start over
        }

        return new Settings();
    }

    public void Save()
    {
        try
        {
            Directory.CreateDirectory(Dir);
            File.WriteAllText(FilePath, JsonSerializer.Serialize(this, Options));
        }
        catch
        {
            // read-only profile: nothing to remember
        }
    }

    public Appearance Puppet(int i)
    {
        while (Puppets.Count <= i)
            Puppets.Add(Appearance.Default(Puppets.Count));
        return Puppets[i];
    }
}
