using System.Text.Json;

namespace AnalisadorLoL;

public class AppSettings
{
    public string Tier { get; set; } = "emerald_plus";
    public string Region { get; set; } = "global";
    public bool OwnedOnly { get; set; } = true;
    public double MasteryWeight { get; set; } = 1.0;
    public string FlashKey { get; set; } = "D";
    public bool AutoRunes { get; set; } = false;
    public bool AutoItemSet { get; set; } = true;
    public bool AutoAccept { get; set; } = false;
    public string OverlayHotkey { get; set; } = "Ctrl+X";
    public bool StartWithWindows { get; set; } = true;
    public bool MinimizeToTray { get; set; } = true;
    public bool LowPriority { get; set; } = true;
    public bool AutoShow { get; set; } = true;
    public bool MinimizeOnGame { get; set; } = false;
    public string? LeaguePath { get; set; }
}

public static class Paths
{
    public static readonly string Roaming = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "AnalisadorLoL");
    public static readonly string Local = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "AnalisadorLoL");
    public static readonly string Cache = Path.Combine(Local, "cache");
    public static readonly string WebView = Path.Combine(Local, "WebView2");
}

public static class Json
{
    public static readonly JsonSerializerOptions Opts = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = true,
        DefaultIgnoreCondition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull,
    };
}

public static class SettingsStore
{
    static readonly string FilePath = Path.Combine(Paths.Roaming, "configuracoes.json");

    public static AppSettings Load()
    {
        if (!File.Exists(FilePath)) return new AppSettings();
        // Algumas tentativas: o arquivo pode estar momentaneamente bloqueado (antivírus, indexação).
        for (int attempt = 1; attempt <= 5; attempt++)
        {
            try { return JsonSerializer.Deserialize<AppSettings>(File.ReadAllText(FilePath), Json.Opts) ?? new(); }
            catch (IOException ex) when (attempt < 5) { Log.Write($"Configurações bloqueadas (tentativa {attempt}): {ex.Message}"); Thread.Sleep(150); }
            catch (Exception ex) { Log.Write("Configurações ilegíveis, usando o padrão: " + ex.Message); break; }
        }
        return new AppSettings();
    }

    public static void Save(AppSettings s)
    {
        Directory.CreateDirectory(Paths.Roaming);
        var json = JsonSerializer.Serialize(s, new JsonSerializerOptions(Json.Opts) { WriteIndented = true });
        var tmp = FilePath + ".tmp";
        for (int attempt = 1; attempt <= 5; attempt++)
        {
            try
            {
                File.WriteAllText(tmp, json);
                File.Move(tmp, FilePath, overwrite: true); // grava inteiro ou nada
                return;
            }
            catch (IOException ex) when (attempt < 5) { Log.Write($"Falha ao salvar configurações (tentativa {attempt}): {ex.Message}"); Thread.Sleep(150); }
        }
    }
}
