namespace AnalisadorLoL;

/// <summary>
/// Lê o time inimigo (campeões + itens já comprados) e indica itens situacionais:
/// anticura, resistência mágica/armadura, penetração contra tanques e visão contra invisíveis.
/// </summary>
public static class Situational
{
    static readonly HashSet<string> Healers = new(StringComparer.OrdinalIgnoreCase)
    {
        "Aatrox", "Soraka", "Vladimir", "Sylas", "DrMundo", "Warwick", "Yuumi", "Nami", "Sona", "Fiddlesticks", "Swain",
        "Briar", "Illaoi", "Olaf", "Volibear", "Trundle", "Gwen", "Zac", "Maokai", "Nidalee", "Milio", "Seraphine", "Belveth", "Kayn",
    };
    static readonly HashSet<string> HardCc = new(StringComparer.OrdinalIgnoreCase)
    {
        "Leona", "Nautilus", "Morgana", "Lissandra", "Sejuani", "Amumu", "Malphite", "Rell", "Alistar", "Thresh", "Blitzcrank",
        "Maokai", "Zac", "Rakan", "Annie", "Veigar", "Ashe", "Pantheon", "Vi", "Skarner", "Zyra", "Neeko", "Lux", "Sett", "Galio",
        "Taric", "Braum", "Poppy", "Ornn", "Cassiopeia", "TwistedFate", "Warwick", "MonkeyKing", "Renata", "Nami", "Bard", "Fiddlesticks",
    };
    static readonly HashSet<string> Stealth = new(StringComparer.OrdinalIgnoreCase)
    {
        "Evelynn", "Twitch", "Akali", "Shaco", "Khazix", "Rengar", "Pyke", "MonkeyKing", "Teemo", "Talon", "Vayne", "Senna",
    };

    /// <summary>
    /// Parte aproximada do dano que é verdadeiro. A Riot não publica esse número: são estimativas
    /// para os campeões cujo dano verdadeiro pesa de verdade (passivas, ultimates, golpes no centro etc.).
    /// </summary>
    static readonly Dictionary<string, double> TrueShare = new(StringComparer.OrdinalIgnoreCase)
    {
        ["Vayne"] = 0.35, ["Fiora"] = 0.35, ["Gwen"] = 0.3, ["Camille"] = 0.25, ["Darius"] = 0.2, ["Garen"] = 0.2,
        ["Olaf"] = 0.2, ["MasterYi"] = 0.2, ["Cassiopeia"] = 0, ["Chogath"] = 0.15, ["Sett"] = 0.15, ["RekSai"] = 0.15,
        ["Pyke"] = 0.15, ["Nilah"] = 0.15, ["Trundle"] = 0.05, ["KogMaw"] = 0.1, ["Shen"] = 0.1, ["Ambessa"] = 0.1,
        ["Kayle"] = 0.1, ["Vex"] = 0, ["Briar"] = 0.05, ["Kalista"] = 0, ["Yorick"] = 0.05, ["Jax"] = 0, ["Rengar"] = 0.05,
        ["Lillia"] = 0.1, ["Leona"] = 0, ["Kindred"] = 0, ["Kled"] = 0, ["Gragas"] = 0, ["Mordekaiser"] = 0,
    };

    /// <summary>
    /// Estima físico/mágico/verdadeiro de um time: perfil de dano do campeão (Data Dragon), ajustado pelos itens
    /// já comprados e pesado por ouro e abates (quem está mais forte conta mais). Tanques e suportes pesam metade.
    /// </summary>
    public static DamageSplit Damage(MetaService meta, IEnumerable<PlayerView> team)
    {
        double phys = 0, magic = 0, tru = 0;
        foreach (var p in team)
        {
            if (!meta.Champions.TryGetValue(p.ChampionId, out var c)) continue;
            int adGold = 0, apGold = 0;
            foreach (var id in p.Items)
            {
                if (!meta.Items.TryGetValue(id, out var it)) continue;
                if (it.Tags.Contains("SpellDamage")) apGold += it.Gold;
                else if (it.Tags.Contains("Damage") || it.Tags.Contains("CriticalStrike") || it.Tags.Contains("AttackSpeed")) adGold += it.Gold;
            }
            double baseAd = c.Attack + c.Magic > 0 ? (double)c.Attack / (c.Attack + c.Magic) : 0.5;
            double share = (baseAd * 1500 + adGold) / (1500 + adGold + apGold);
            double t = TrueShare.GetValueOrDefault(c.Key, 0);
            double weight = (c.Tags[0] == "Tank" || (p.Position == Pos.Support && c.Tags.Contains("Support")) ? 0.5 : 1.0)
                            * (1 + p.ItemGold / 4000.0 + p.Kills * 0.05);
            phys += share * (1 - t) * weight;
            magic += (1 - share) * (1 - t) * weight;
            tru += t * weight;
        }
        var total = phys + magic + tru;
        if (total <= 0) return new DamageSplit(50, 50, 0);
        int pp = (int)Math.Round(phys / total * 100), tp = (int)Math.Round(tru / total * 100);
        return new DamageSplit(pp, 100 - pp - tp, tp);
    }

    enum Profile { Ap, Adc, Ad, Tank, Support }

    // Itens por necessidade e por perfil do seu campeão (IDs conferidos no patch 16.22).
    static readonly Dictionary<Profile, int[]> AntiHeal = new()
    {
        [Profile.Ap] = [3165, 3916], [Profile.Adc] = [3033, 3123], [Profile.Ad] = [6609, 3123], [Profile.Tank] = [3075, 3076], [Profile.Support] = [3916, 3076],
    };
    static readonly Dictionary<Profile, int[]> VsMagic = new()
    {
        [Profile.Ap] = [3102], [Profile.Adc] = [3156, 3139], [Profile.Ad] = [3156, 3065, 2504], [Profile.Tank] = [4401, 2504, 3065], [Profile.Support] = [3222, 3190],
    };
    static readonly Dictionary<Profile, int[]> VsPhysical = new()
    {
        [Profile.Ap] = [3157], [Profile.Adc] = [3026, 6673], [Profile.Ad] = [6333, 3143], [Profile.Tank] = [3143, 3110, 3075], [Profile.Support] = [3190, 3109],
    };
    static readonly Dictionary<Profile, int[]> VsTanks = new()
    {
        [Profile.Ap] = [3135, 6653], [Profile.Adc] = [3036, 3153], [Profile.Ad] = [3071, 3036], [Profile.Tank] = [], [Profile.Support] = [],
    };
    const int Mercurys = 3111, Steelcaps = 3047, Scimitar = 3139, ControlWard = 2055, Oracle = 3364;

    static Profile ProfileOf(ChampInfo c, string? pos)
    {
        if (pos == Pos.Support && (c.Tags.Contains("Support") || c.Tags[0] == "Tank")) return Profile.Support;
        if (c.Tags[0] == "Tank") return Profile.Tank;
        if (c.Tags[0] == "Marksman" || pos == Pos.Adc) return c.Magic > c.Attack ? Profile.Ap : Profile.Adc;
        return c.Magic > c.Attack ? Profile.Ap : Profile.Ad;
    }

    public static (ThreatInfo, List<SituationalItem>) Analyze(MetaService meta, PlayerView me, List<PlayerView> enemies, ChampionBuild? build, string mode = Mode.Classic)
    {
        var threats = new ThreatInfo();

        foreach (var e in enemies)
        {
            if (!meta.Champions.TryGetValue(e.ChampionId, out var c)) continue;
            int adGold = 0, apGold = 0, defGold = 0, sustainGold = 0;
            foreach (var id in e.Items)
            {
                if (!meta.Items.TryGetValue(id, out var it)) continue;
                if (it.Tags.Contains("SpellDamage")) apGold += it.Gold;
                else if (it.Tags.Contains("Damage") || it.Tags.Contains("CriticalStrike") || it.Tags.Contains("AttackSpeed")) adGold += it.Gold;
                else if (it.Tags.Contains("Health") || it.Tags.Contains("Armor") || it.Tags.Contains("SpellBlock")) defGold += it.Gold;
                if (it.Tags.Contains("LifeSteal") || it.Tags.Contains("SpellVamp")) sustainGold += it.Gold;
            }
            if (Healers.Contains(c.Key) || sustainGold >= 2500) threats.Healers.Add(e.ChampionId);
            if (c.Tags[0] == "Tank" || defGold >= 4000) threats.Tanks.Add(e.ChampionId);
            if (HardCc.Contains(c.Key)) threats.HardCc.Add(e.ChampionId);
            if (Stealth.Contains(c.Key)) threats.Stealth.Add(e.ChampionId);
        }
        var dmg = Damage(meta, enemies);
        (threats.PhysicalPct, threats.MagicPct, threats.TruePct) = (dmg.Physical, dmg.Magic, dmg.True);

        var result = new List<SituationalItem>();
        if (!meta.Champions.TryGetValue(me.ChampionId, out var mine)) return (threats, result);
        var profile = ProfileOf(mine, me.Position);
        var owned = me.Items.ToHashSet();
        var popular = new HashSet<int>((build?.Late ?? []).Concat(build?.Core ?? []).Concat(build?.Boots ?? []).SelectMany(s => s.Ids));
        bool hasBoots = me.Items.Any(id => meta.Items.TryGetValue(id, out var it) && it.Tags.Contains("Boots") && it.Gold > 300);
        string Names(IEnumerable<int> ids) => string.Join(", ", ids.Take(3).Select(id => meta.Champions.TryGetValue(id, out var c) ? c.Name : "?"));

        void Add(IEnumerable<int> ids, string reason, int max = 2)
        {
            // Cada mapa tem a sua versão dos itens (na Arena, 3031 vira 223031)
            var map = Mode.MapId(mode);
            int Resolve(int id) => mode == Mode.Arena && meta.Items.ContainsKey(220000 + id) ? 220000 + id : id;
            foreach (var id in ids.Select(Resolve).Where(id => meta.Items.TryGetValue(id, out var it) && it.Purchasable && it.Maps.Contains(map) && !owned.Contains(id))
                         .OrderByDescending(popular.Contains).Take(max))
                if (result.All(r => r.ItemId != id))
                    result.Add(new SituationalItem { ItemId = id, Reason = reason, Popular = popular.Contains(id) });
        }

        if (threats.Healers.Count >= 2 || (threats.Healers.Count == 1 && enemies.Count <= 2))
            Add(AntiHeal[profile], $"Anticura: {Names(threats.Healers)} se curam muito", 1);
        if (threats.MagicPct >= 60)
        {
            Add(VsMagic[profile], $"Inimigos com {threats.MagicPct}% de dano mágico");
            if (!hasBoots && profile != Profile.Adc) Add([Mercurys], "Botas contra dano mágico e controle", 1);
        }
        if (threats.PhysicalPct >= 65)
        {
            Add(VsPhysical[profile], $"Inimigos com {threats.PhysicalPct}% de dano físico");
            if (!hasBoots && profile != Profile.Ap && profile != Profile.Adc) Add([Steelcaps], "Botas contra ataques básicos", 1);
        }
        if (threats.Tanks.Count >= 2) Add(VsTanks[profile], $"Tanques: {Names(threats.Tanks)}");
        if (threats.HardCc.Count >= 3)
        {
            if (!hasBoots) Add([Mercurys], $"Muito controle: {Names(threats.HardCc)}", 1);
            if (profile == Profile.Adc) Add([Scimitar], "Remove controle de grupo", 1);
        }
        if (threats.Stealth.Count > 0) Add([ControlWard, Oracle], $"Invisíveis: {Names(threats.Stealth)}", 2);

        return (threats, result.OrderByDescending(r => r.Popular).Take(6).ToList());
    }
}
