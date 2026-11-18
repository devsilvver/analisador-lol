namespace AnalisadorLoL;

// Rotas no formato usado internamente (igual ao op.gg).
public static class Pos
{
    public const string Top = "TOP", Jungle = "JUNGLE", Mid = "MID", Adc = "ADC", Support = "SUPPORT";
    public static readonly string[] All = [Top, Jungle, Mid, Adc, Support];

    /// <summary>Converte nomes do cliente (top/middle/bottom/utility) e da Live API (MIDDLE/BOTTOM/UTILITY).</summary>
    public static string? From(string? raw) => raw?.Trim().ToUpperInvariant() switch
    {
        "TOP" => Top,
        "JUNGLE" => Jungle,
        "MIDDLE" or "MID" => Mid,
        "BOTTOM" or "ADC" => Adc,
        "UTILITY" or "SUPPORT" => Support,
        _ => null,
    };

    public static string Url(string pos) => pos.ToLowerInvariant();
}

// ---------- Dados estáticos (Data Dragon) ----------
public record ChampInfo(int Id, string Key, string Name, string Title, string[] Tags, int Attack, int Magic, int Defense, string Icon);
public record ItemInfo(int Id, string Name, int Gold, string Icon, string[] Tags, string Plaintext, bool Purchasable, bool OnRift, int[] From, int[] Into, int Depth, int[] Maps);
/// <summary>Aprimoramento (Arena e ARAM com aprimoramentos). Rarity: prata, ouro, prismatico.</summary>
public record AugmentInfo(int Id, string Name, string Icon, string Rarity);
public record RuneInfo(int Id, string Name, string Icon, string Desc, int TreeId);
public record RuneTree(int Id, string Name, string Icon);
public record SpellInfo(int Id, string Key, string Name, string Icon);

public record StaticDto(string Version, IEnumerable<ChampInfo> Champions, IEnumerable<ItemInfo> Items,
    IEnumerable<RuneInfo> Runes, IEnumerable<RuneTree> Trees, IEnumerable<SpellInfo> Spells, IEnumerable<AugmentInfo> Augments);

// ---------- Dados do meta (op.gg) ----------
public record TierEntry(int ChampionId, string Position, double WinRate, double PickRate, double BanRate,
    double RoleRate, int Play, int Tier, int Rank);

public record ItemSet(int[] Ids, double WinRate, double PickRate, int Play)
{
    /// <summary>Arena: posição média de quem comprou.</summary>
    public double? AvgPlace { get; init; }
}
public record RunePage(int PrimaryStyle, int SubStyle, int[] Primary, int[] Secondary, int[] Shards, double WinRate, double PickRate, int Play);
public record SkillOrder(string[] Order, string[] Priority, double WinRate, double PickRate, int Play);
/// <summary>Confronto do ponto de vista do campeão dono da build.</summary>
public record Matchup(int ChampionId, int Play, double WinRate);

public record ChampionBuild(int ChampionId, string Position, string Patch, double WinRate, double PickRate, double BanRate,
    int Tier, int Rank, int Play, ItemSet[] Starters, ItemSet[] Core, ItemSet[] Boots, ItemSet[] Late,
    ItemSet[] Spells, RunePage[] Runes, SkillOrder[] Skills, Matchup[] Counters)
{
    public string Mode { get; init; } = AnalisadorLoL.Mode.Classic;
    /// <summary>Aprimoramentos por raridade (1 prata, 4 ouro, 8 prismático).</summary>
    public AugmentGroup[] Augments { get; init; } = [];
    public ItemSet[] Prismatic { get; init; } = [];
    public Synergy[] Synergies { get; init; } = [];
    /// <summary>Arena: posição média e taxa de 1º lugar.</summary>
    public double? AvgPlace { get; init; }
    public double? FirstRate { get; init; }
}

public record AugmentStat(int Id, double WinRate, double PickRate, int Play, double? AvgPlace);
public record AugmentGroup(int Rarity, AugmentStat[] Augments);
/// <summary>Arena: desempenho do campeão quando faz dupla com outro.</summary>
public record Synergy(int ChampionId, int Play, double WinRate, double? AvgPlace);
/// <summary>Linha da tier list de um modo sem rotas (ARAM, Arena, URF...).</summary>
public record ModeEntry(int ChampionId, double WinRate, double PickRate, double BanRate, int Play, int Tier, int Rank, double? AvgPlace, double? FirstRate);

// ---------- Estado enviado para a tela ----------
public record RankedQueue(string Queue, string Tier, string Division, int Lp, int Wins, int Losses, string HighestTier, string HighestDivision);
public record MasteryEntry(int ChampionId, int Level, int Points);
public record MatchEntry(long GameId, int QueueId, string GameMode, int ChampionId, bool Win, int Kills, int Deaths, int Assists,
    int Cs, int Gold, int Duration, long Created, int[] Items, int Placement, int[] Augments, bool Remake);

/// <summary>Perfil do jogador para a tela inicial (lido do cliente).</summary>
public class ProfileView
{
    public string Name { get; set; } = "";
    public string Tag { get; set; } = "";
    public int Level { get; set; }
    public int LevelPct { get; set; }
    public string Icon { get; set; } = "";
    public List<RankedQueue> Ranks { get; set; } = [];
    public List<MasteryEntry> Mastery { get; set; } = [];
    public List<MatchEntry> Matches { get; set; } = [];
    /// <summary>Quando foi lido do cliente (para mostrar o perfil guardado com o cliente fechado).</summary>
    public DateTime UpdatedAt { get; set; }
}

public class AppState
{
    public ProfileView? Profile { get; set; }
    public ClientInfo Client { get; set; } = new();
    /// <summary>Modo da fila atual (classic, aram, mayhem, arena, urf, nexus, unsupported).</summary>
    public string Mode { get; set; } = AnalisadorLoL.Mode.Classic;
    public string ModeName { get; set; } = "";
    public string Patch { get; set; } = "";
    public string? MetaError { get; set; }
    public ChampSelectView? ChampSelect { get; set; }
    public GameView? Game { get; set; }
}

public class ClientInfo
{
    public bool Connected { get; set; }
    public string Phase { get; set; } = "Desconectado";
    public string? Summoner { get; set; }
    public bool LiveGame { get; set; }
}

/// <summary>Arena: companheiro de time visto na seleção de campeões.</summary>
public record ArenaMate(string Puuid, string? Name, int ChampionId);

public record RankInfo(string Tier, string Division, int Lp, int Wins, int Losses);

/// <summary>Grupo pré-formado (duo, trio…). Exact = informado pelo cliente; senão é estimado pelo histórico.</summary>
public record PartyTag(int Group, int Size, bool Exact, int Games);

public class Slot
{
    public int CellId { get; set; }
    public int ChampionId { get; set; }
    public bool Locked { get; set; }
    public bool IsMe { get; set; }
    public string? Position { get; set; }
    public double? PositionConfidence { get; set; }
    public string? Name { get; set; }
    public RankInfo? Rank { get; set; }
    public PartyTag? Party { get; set; }
    [System.Text.Json.Serialization.JsonIgnore] public string? Puuid { get; set; }
}

public class Suggestion
{
    public int ChampionId { get; set; }
    public double Score { get; set; }
    public double WinRate { get; set; }
    public double PickRate { get; set; }
    public int Tier { get; set; }
    public double? MatchupWinRate { get; set; }
    public int? MatchupGames { get; set; }
    public bool Owned { get; set; } = true;
    /// <summary>ARAM: onde o campeão está ("seu", "banco", "aliado").</summary>
    public string? Source { get; set; }
    public double? AvgPlace { get; set; }
    public int Mastery { get; set; }
    public List<string> Reasons { get; set; } = [];
}

public class ChampSelectView
{
    public string? MyPosition { get; set; }
    public string PositionSource { get; set; } = "desconhecida";
    public int MyChampionId { get; set; }
    public bool MyLocked { get; set; }
    /// <summary>Fase final da seleção: todos os campeões já estão definidos.</summary>
    public bool Final { get; set; }
    public string Stage { get; set; } = "";
    public bool MyTurnToBan { get; set; }
    public bool MyTurnToPick { get; set; }
    public int SecondsLeft { get; set; }
    public List<Slot> Allies { get; set; } = [];
    public List<Slot> Enemies { get; set; } = [];
    public List<int> Bans { get; set; } = [];
    public Slot? LaneOpponent { get; set; }
    public List<Suggestion> Suggestions { get; set; } = [];
    public List<Suggestion> BanSuggestions { get; set; } = [];
    public List<Matchup> LaneCounters { get; set; } = [];
    public Matchup? MyMatchup { get; set; }
    public ChampionBuild? MyBuild { get; set; }
    public DamageSplit? AllyDamage { get; set; }
    public DamageSplit? EnemyDamage { get; set; }
    public string Mode { get; set; } = AnalisadorLoL.Mode.Classic;
    /// <summary>ARAM: campeões no banco (dá para trocar).</summary>
    public List<int> Bench { get; set; } = [];
    public int RerollsRemaining { get; set; }
    public string? Notice { get; set; }
}

public class PlayerView
{
    public string RiotId { get; set; } = "";
    public int ChampionId { get; set; }
    public string Team { get; set; } = "";
    public string? Position { get; set; }
    public int Level { get; set; }
    public int Kills { get; set; }
    public int Deaths { get; set; }
    public int Assists { get; set; }
    public int Cs { get; set; }
    public int[] Items { get; set; } = [];
    public int ItemGold { get; set; }
    public bool IsDead { get; set; }
    public double Respawn { get; set; }
    public int[] Spells { get; set; } = [];
    public bool IsMe { get; set; }
    public RankInfo? Rank { get; set; }
    public PartyTag? Party { get; set; }
    /// <summary>Arena: número da dupla.</summary>
    public int? Subteam { get; set; }
    [System.Text.Json.Serialization.JsonIgnore] public int Index { get; set; }
}

public class BuildStep
{
    public int ItemId { get; set; }
    public string Status { get; set; } = "falta"; // "comprado" | "em-progresso" | "falta"
}

public class SituationalItem
{
    public int ItemId { get; set; }
    public string Reason { get; set; } = "";
    public bool Popular { get; set; }
}

/// <summary>Divisão estimada do dano de um time, em %.</summary>
public record DamageSplit(int Physical, int Magic, int True);

public class ThreatInfo
{
    public int PhysicalPct { get; set; }
    public int MagicPct { get; set; }
    public int TruePct { get; set; }
    public List<int> Healers { get; set; } = [];
    public List<int> Tanks { get; set; } = [];
    public List<int> HardCc { get; set; } = [];
    public List<int> Stealth { get; set; } = [];
}

/// <summary>Placar de objetivos de um time (lido dos eventos da partida).</summary>
public class TeamObjectives
{
    public int Kills { get; set; }
    public int Towers { get; set; }
    public int Inhibitors { get; set; }
    public int Barons { get; set; }
    public int Heralds { get; set; }
    public int Grubs { get; set; }
    public List<string> Dragons { get; set; } = [];
}

public class GameView
{
    public double GameTime { get; set; }
    public string GameMode { get; set; } = "";
    public string? MyPosition { get; set; }
    public PlayerView? Me { get; set; }
    public PlayerView? LaneOpponent { get; set; }
    public List<PlayerView> Allies { get; set; } = [];
    public List<PlayerView> Enemies { get; set; } = [];
    public ChampionBuild? Build { get; set; }
    public List<BuildStep> CorePath { get; set; } = [];
    public List<BuildStep>? AltCorePath { get; set; }
    public double AltCoreWinRate { get; set; }
    public BuildStep? Boots { get; set; }
    public List<BuildStep> LateOptions { get; set; } = [];
    public int? NextItem { get; set; }
    public List<SituationalItem> Situational { get; set; } = [];
    public ThreatInfo Threats { get; set; } = new();
    public DamageSplit? AllyDamage { get; set; }
    public Matchup? MyMatchup { get; set; }
    public string Mode { get; set; } = AnalisadorLoL.Mode.Classic;
    /// <summary>Arena: o app sabe quem é o seu time (visto na seleção)?</summary>
    public bool ArenaTeamKnown { get; set; } = true;
    /// <summary>Arena: jogadores por time (2 ou 3).</summary>
    public int ArenaTeamSize { get; set; }
    public TeamObjectives AllyObjectives { get; set; } = new();
    public TeamObjectives EnemyObjectives { get; set; } = new();
    public int AllyGold { get; set; }
    public int EnemyGold { get; set; }
    public string? NextSkill { get; set; }
    public int? NextSkillIndex { get; set; }
    /// <summary>Elo/região das estatísticas da build mostrada e se é a mesma enviada para a loja do jogo.</summary>
    public string BuildTier { get; set; } = "";
    public string BuildRegion { get; set; } = "";
    public bool SameAsShop { get; set; }
}
