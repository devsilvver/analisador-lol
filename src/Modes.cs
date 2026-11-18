namespace AnalisadorLoL;

/// <summary>
/// Modos de jogo. Cada um usa as estatísticas do próprio modo no op.gg:
/// ranqueada (por rota), ARAM, ARAM com aprimoramentos (Mayhem, nome interno "Kiwi"), Arena ("Cherry"), URF e Nexus Blitz.
/// </summary>
public static class Mode
{
    public const string Classic = "classic", Aram = "aram", Mayhem = "mayhem", Arena = "arena", Urf = "urf", Nexus = "nexus", Unsupported = "unsupported";

    /// <summary>Pelo que o cliente informa da fila (gameflow session → gameData.queue).</summary>
    public static string FromQueue(string? gameMode, int queueId, string? description)
    {
        var d = (description ?? "").ToLowerInvariant();
        var gm = (gameMode ?? "").ToUpperInvariant();
        if (gm == "KIWI" || d.Contains("mayhem") || d.Contains("caos")) return Mayhem;
        return FromGameMode(gm);
    }

    /// <summary>Pelo "gameMode" da API da partida ou do cliente.</summary>
    public static string FromGameMode(string? gameMode) => (gameMode ?? "").ToUpperInvariant() switch
    {
        "ARAM" => Aram,
        "KIWI" => Mayhem,
        "CHERRY" => Arena,
        "URF" or "ARURF" => Urf,
        "NEXUSBLITZ" => Nexus,
        "STRAWBERRY" or "TUTORIAL" or "TUTORIAL_MODULE_1" or "TUTORIAL_MODULE_2" or "TUTORIAL_MODULE_3" => Unsupported,
        _ => Classic, // ranqueada, normal, escolha rápida, treino, Todos por Um etc.: estatísticas da ranqueada
    };

    /// <summary>Nome do modo na API do op.gg.</summary>
    public static string OpGg(string mode) => mode switch
    {
        Aram => "aram", Mayhem => "aram_mayhem_classic", Arena => "arena", Urf => "urf", Nexus => "nexus_blitz", _ => "ranked",
    };

    public static bool HasLanes(string mode) => mode == Classic;
    public static bool HasRunes(string mode) => mode != Arena;
    public static bool HasAugments(string mode) => mode is Mayhem or Arena;
    /// <summary>ARAM e Mayhem: campeão aleatório, com banco de trocas.</summary>
    public static bool IsRandom(string mode) => mode is Aram or Mayhem;

    /// <summary>Mapa usado no conjunto de itens enviado para a loja.</summary>
    public static int MapId(string mode) => mode switch { Aram or Mayhem => 12, Arena => 30, Nexus => 21, _ => 11 };

    public static string Name(string mode) => mode switch
    {
        Aram => "ARAM", Mayhem => "ARAM com aprimoramentos", Arena => "Arena", Urf => "URF", Nexus => "Nexus Blitz",
        Unsupported => "Modo sem estatísticas", _ => "Summoner's Rift",
    };

    public static readonly string[] Supported = [Classic, Aram, Mayhem, Arena, Urf, Nexus];
}
