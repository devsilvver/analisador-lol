using System.Collections.Concurrent;
using System.Net;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;

namespace AnalisadorLoL;

/// <summary>
/// Dados estáticos (Data Dragon, oficial da Riot) e estatísticas do meta (op.gg).
/// Tudo fica em cache na memória e em disco: o meta é baixado no máximo a cada 6 horas.
/// </summary>
public sealed partial class MetaService : IDisposable
{
    const string DDragon = "https://ddragon.leagueoflegends.com";
    const string OpGg = "https://lol-api-champion.op.gg/api";
    static readonly TimeSpan MetaTtl = TimeSpan.FromHours(6);

    readonly HttpClient web;
    readonly ConcurrentDictionary<string, Lazy<Task<object?>>> inflight = new();
    readonly ConcurrentDictionary<string, (DateTime at, object? value)> memo = new();

    public string Version { get; private set; } = "";
    public string Patch { get; private set; } = "";
    public Dictionary<int, ChampInfo> Champions { get; } = [];
    public Dictionary<string, int> ChampionByKey { get; } = new(StringComparer.OrdinalIgnoreCase);
    public Dictionary<int, ItemInfo> Items { get; } = [];
    public Dictionary<int, RuneInfo> Runes { get; } = [];
    public Dictionary<int, RuneTree> Trees { get; } = [];
    public Dictionary<int, SpellInfo> Spells { get; } = [];
    public Dictionary<string, int> SpellByKey { get; } = new(StringComparer.OrdinalIgnoreCase);

    public MetaService()
    {
        var handler = new SocketsHttpHandler
        {
            AutomaticDecompression = DecompressionMethods.All,
            PooledConnectionIdleTimeout = TimeSpan.FromSeconds(20),
        };
        web = new HttpClient(handler) { Timeout = TimeSpan.FromSeconds(15) };
        web.DefaultRequestHeaders.UserAgent.ParseAdd("Mozilla/5.0 (Windows NT 10.0; Win64; x64) AnalisadorLoL/1.0");
        web.DefaultRequestHeaders.Accept.ParseAdd("application/json");
    }

    // ------------------------------------------------------------------ Data Dragon

    public async Task LoadStaticAsync()
    {
        string version;
        try
        {
            var versions = JsonNode.Parse(await web.GetStringAsync($"{DDragon}/api/versions.json"))!.AsArray();
            version = versions[0]!.GetValue<string>();
        }
        catch
        {
            // Sem internet: usa a última versão baixada.
            var dir = Path.Combine(Paths.Cache, "ddragon");
            version = Directory.Exists(dir)
                ? Directory.GetDirectories(dir).Select(Path.GetFileName).OrderByDescending(v => v, StringComparer.Ordinal).FirstOrDefault() ?? throw new Exception("Sem internet e sem cache.")
                : throw new Exception("Sem conexão com a internet para baixar os dados do jogo.");
        }
        Version = version;
        Patch = string.Join('.', version.Split('.').Take(2));

        async Task<JsonNode> Static(string file)
        {
            var path = Path.Combine(Paths.Cache, "ddragon", version, file);
            if (!File.Exists(path))
            {
                var txt = await web.GetStringAsync($"{DDragon}/cdn/{version}/data/pt_BR/{file}");
                Directory.CreateDirectory(Path.GetDirectoryName(path)!);
                await File.WriteAllTextAsync(path, txt);
            }
            return JsonNode.Parse(await File.ReadAllTextAsync(path))!;
        }

        var champs = Static("champion.json");
        var items = Static("item.json");
        var runes = Static("runesReforged.json");
        var spells = Static("summoner.json");
        await Task.WhenAll(champs, items, runes, spells);

        foreach (var (_, c) in champs.Result["data"]!.AsObject())
        {
            var id = int.Parse(c!["key"]!.GetValue<string>());
            var info = c["info"]!;
            var ch = new ChampInfo(id, c["id"]!.GetValue<string>(), c["name"]!.GetValue<string>(), c["title"]!.GetValue<string>(),
                c["tags"]!.AsArray().Select(t => t!.GetValue<string>()).ToArray(),
                info["attack"]!.GetValue<int>(), info["magic"]!.GetValue<int>(), info["defense"]!.GetValue<int>(),
                $"{DDragon}/cdn/{version}/img/champion/{c["image"]!["full"]!.GetValue<string>()}");
            Champions[id] = ch;
            ChampionByKey[ch.Key] = id;
        }

        foreach (var (key, it) in items.Result["data"]!.AsObject())
        {
            if (!int.TryParse(key, out var id) || it == null) continue;
            var gold = it["gold"]!;
            int[] Ids(string field) => it[field] is JsonArray a ? a.Select(x => int.TryParse(x!.GetValue<string>(), out var v) ? v : 0).Where(v => v > 0).ToArray() : [];
            Items[id] = new ItemInfo(id, it["name"]!.GetValue<string>(), gold["total"]!.GetValue<int>(),
                $"{DDragon}/cdn/{version}/img/item/{id}.png",
                it["tags"] is JsonArray tg ? tg.Select(t => t!.GetValue<string>()).ToArray() : [],
                it["plaintext"]?.GetValue<string>() ?? "",
                gold["purchasable"]?.GetValue<bool>() == true && it["inStore"]?.GetValue<bool>() != false,
                it["maps"]?["11"]?.GetValue<bool>() == true,
                Ids("from"), Ids("into"), it["depth"]?.GetValue<int>() ?? 1,
                it["maps"] is JsonObject maps ? maps.Where(m => m.Value?.GetValue<bool>() == true).Select(m => int.Parse(m.Key)).ToArray() : []);
        }

        foreach (var tree in runes.Result.AsArray())
        {
            var treeId = tree!["id"]!.GetValue<int>();
            Trees[treeId] = new RuneTree(treeId, tree["name"]!.GetValue<string>(), $"{DDragon}/cdn/img/{tree["icon"]!.GetValue<string>()}");
            foreach (var slot in tree["slots"]!.AsArray())
                foreach (var r in slot!["runes"]!.AsArray())
                {
                    var id = r!["id"]!.GetValue<int>();
                    Runes[id] = new RuneInfo(id, r["name"]!.GetValue<string>(), $"{DDragon}/cdn/img/{r["icon"]!.GetValue<string>()}",
                        StripHtml(r["shortDesc"]?.GetValue<string>() ?? ""), treeId);
                }
        }

        foreach (var (key, s) in spells.Result["data"]!.AsObject())
        {
            var id = int.Parse(s!["key"]!.GetValue<string>());
            Spells[id] = new SpellInfo(id, key, s["name"]!.GetValue<string>(), $"{DDragon}/cdn/{version}/img/spell/{s["image"]!["full"]!.GetValue<string>()}");
            SpellByKey[key] = id;
        }

        await LoadAugmentsAsync(version);
    }

    public Dictionary<int, AugmentInfo> Augments { get; } = [];

    /// <summary>Nomes e ícones dos aprimoramentos (Arena e ARAM com aprimoramentos), em pt-BR, do CommunityDragon.</summary>
    async Task LoadAugmentsAsync(string version)
    {
        const string CDragon = "https://raw.communitydragon.org/latest/plugins/rcp-be-lol-game-data/global";
        var path = Path.Combine(Paths.Cache, "ddragon", version, "cherry-augments.json");
        try
        {
            if (!File.Exists(path))
            {
                var txt = await web.GetStringAsync($"{CDragon}/pt_br/v1/cherry-augments.json");
                await File.WriteAllTextAsync(path, txt);
            }
            foreach (var a in JsonNode.Parse(await File.ReadAllTextAsync(path))!.AsArray())
            {
                var id = a?["id"]?.GetValue<int>() ?? 0;
                var icon = a?["augmentSmallIconPath"]?.GetValue<string>() ?? "";
                const string prefix = "/lol-game-data/assets/";
                if (id == 0 || icon.Length == 0) continue;
                var url = icon.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)
                    ? $"{CDragon}/default/{icon[prefix.Length..].ToLowerInvariant()}" : "";
                var rarity = a!["rarity"]?.GetValue<string>() switch { "kGold" => "ouro", "kPrismatic" => "prismatico", "kSilver" => "prata", _ => "evento" };
                Augments[id] = new AugmentInfo(id, a["nameTRA"]?.GetValue<string>() ?? $"#{id}", url, rarity);
            }
        }
        catch (Exception ex) { Log.Write("Aprimoramentos indisponíveis: " + ex.Message); }
    }

    [GeneratedRegex("<[^>]+>")] private static partial Regex HtmlTag();
    static string StripHtml(string s) => WebUtility.HtmlDecode(HtmlTag().Replace(s, ""));

    // Itens de todos os mapas jogáveis (Summoner's Rift, ARAM, Nexus Blitz e os próprios da Arena)
    public StaticDto ToDto() => new(Version, Champions.Values.OrderBy(c => c.Name),
        Items.Values.Where(i => i.Maps.Any(m => m is 11 or 12 or 21 or 30)), Runes.Values, Trees.Values, Spells.Values, Augments.Values);

    // ------------------------------------------------------------------ op.gg

    public void ClearMetaCache()
    {
        memo.Clear();
        try { if (Directory.Exists(Path.Combine(Paths.Cache, "meta"))) Directory.Delete(Path.Combine(Paths.Cache, "meta"), true); } catch { }
    }

    /// <summary>Busca com cache (memória + disco) e sem requisições duplicadas em paralelo.</summary>
    async Task<T?> Cached<T>(string key, string url, Func<JsonNode, T> parse) where T : class
    {
        if (memo.TryGetValue(key, out var m) && DateTime.UtcNow - m.at < MetaTtl) return m.value as T;

        var lazy = inflight.GetOrAdd(key, _ => new Lazy<Task<object?>>(async () =>
        {
            var file = Path.Combine(Paths.Cache, "meta", key + ".json");
            string? txt = null;
            var fromDisk = File.Exists(file) && DateTime.UtcNow - File.GetLastWriteTimeUtc(file) < MetaTtl;
            if (fromDisk) txt = await File.ReadAllTextAsync(file);
            else
            {
                try
                {
                    txt = await web.GetStringAsync(url);
                    Directory.CreateDirectory(Path.GetDirectoryName(file)!);
                    await File.WriteAllTextAsync(file, txt);
                }
                catch
                {
                    if (File.Exists(file)) txt = await File.ReadAllTextAsync(file); // offline: usa o antigo
                }
            }
            object? value = null;
            if (txt != null) try { value = parse(JsonNode.Parse(txt)!); } catch { }
            // Falha: tenta de novo em 1 minuto em vez de esperar as 6 horas do cache.
            memo[key] = (value != null ? DateTime.UtcNow : DateTime.UtcNow - MetaTtl + TimeSpan.FromMinutes(1), value);
            return value;
        }));
        try { return await lazy.Value as T; }
        finally { inflight.TryRemove(key, out _); }
    }

    public Task<List<TierEntry>?> GetTierListAsync(string region, string tier) =>
        Cached($"tier_{region}_{tier}", $"{OpGg}/{region}/champions/ranked?tier={tier}", node =>
        {
            var list = new List<TierEntry>();
            var patch = node["meta"]?["version"]?.GetValue<string>();
            if (!string.IsNullOrEmpty(patch)) Patch = patch;
            foreach (var c in node["data"]!.AsArray())
            {
                var id = c!["id"]!.GetValue<int>();
                if (c["positions"] is not JsonArray positions) continue;
                foreach (var p in positions)
                {
                    var pos = Pos.From(p!["name"]?.GetValue<string>());
                    var st = p["stats"];
                    if (pos == null || st == null) continue;
                    list.Add(new TierEntry(id, pos, D(st["win_rate"]), D(st["pick_rate"]), D(st["ban_rate"]), D(st["role_rate"]),
                        I(st["play"]), I(st["tier_data"]?["tier"]), I(st["tier_data"]?["rank"])));
                }
            }
            return list;
        });

    /// <summary>
    /// Build de um campeão na rota. O resumo (vitórias, escolha, tier) vem da tier list quando ela tem amostra maior:
    /// assim a sugestão, a prévia da build e a tier list mostram o mesmo número.
    /// </summary>
    public async Task<ChampionBuild?> GetBuildAsync(int championId, string position, string region, string tier)
    {
        var b = await GetBuildRawAsync(championId, position, region, tier);
        if (b == null) return null;
        var t = (await GetTierListAsync(region, tier))?.FirstOrDefault(e => e.ChampionId == championId && e.Position == position);
        return t == null || t.Play <= b.Play ? b
            : b with { WinRate = t.WinRate, PickRate = t.PickRate, BanRate = t.BanRate, Tier = t.Tier, Rank = t.Rank, Play = t.Play };
    }

    Task<ChampionBuild?> GetBuildRawAsync(int championId, string position, string region, string tier) =>
        Cached($"build_{region}_{tier}_{championId}_{position}", $"{OpGg}/{region}/champions/ranked/{championId}/{Pos.Url(position)}?tier={tier}", node =>
        {
            var d = node["data"]!;
            var posStats = (d["summary"]?["positions"] as JsonArray)?.FirstOrDefault(p => Pos.From(p?["name"]?.GetValue<string>()) == position)?["stats"]
                           ?? d["summary"]?["average_stats"];
            return ParseBuild(d, posStats, championId, position, AnalisadorLoL.Mode.Classic);
        });

    /// <summary>Tier list de um modo sem rotas (ARAM, ARAM com aprimoramentos, Arena, URF, Nexus Blitz).</summary>
    public Task<List<ModeEntry>?> GetModeListAsync(string mode, string region) =>
        Cached($"mode_{region}_{mode}", $"{OpGg}/{region}/champions/{AnalisadorLoL.Mode.OpGg(mode)}", node =>
        {
            var list = new List<ModeEntry>();
            foreach (var c in node["data"]!.AsArray())
            {
                var st = c?["average_stats"];
                if (c == null || st == null) continue;
                var play = I(st["play"]);
                if (play == 0) continue;
                list.Add(new ModeEntry(I(c["id"]), st["win_rate"] != null ? D(st["win_rate"]) : Wr(st), D(st["pick_rate"]), D(st["ban_rate"]), play,
                    I(st["tier_data"]?["tier"] ?? st["tier"]), I(st["tier_data"]?["rank"] ?? st["rank"]),
                    st["total_place"] != null ? D(st["total_place"]) / play : null, st["first_place"] != null ? D(st["first_place"]) / play : null));
            }
            return list;
        });

    /// <summary>Build de um campeão num modo sem rotas.</summary>
    public Task<ChampionBuild?> GetModeBuildAsync(int championId, string mode, string region)
    {
        var m = AnalisadorLoL.Mode.OpGg(mode);
        var url = mode == AnalisadorLoL.Mode.Arena ? $"{OpGg}/{region}/champions/arena/{championId}" : $"{OpGg}/{region}/champions/{m}/{championId}/none";
        return Cached($"modebuild_{region}_{mode}_{championId}", url, node =>
        {
            var d = node["data"]!;
            return ParseBuild(d, d["summary"]?["average_stats"], championId, mode.ToUpperInvariant(), mode);
        });
    }

    ChampionBuild ParseBuild(JsonNode d, JsonNode? posStats, int championId, string position, string mode)
    {
        {
            static ItemSet[] Sets(JsonNode? arr) => arr is JsonArray a
                ? a.Select(x => new ItemSet(x!["ids"]!.AsArray().Select(i => i!.GetValue<int>()).ToArray(), Wr(x), D(x["pick_rate"]), I(x["play"])) { AvgPlace = Place(x) }).ToArray()
                : [];

            var runes = (d["runes"] as JsonArray ?? []).Select(r => new RunePage(I(r!["primary_page_id"]), I(r["secondary_page_id"]),
                Ints(r["primary_rune_ids"]), Ints(r["secondary_rune_ids"]), Ints(r["stat_mod_ids"]), Wr(r), D(r["pick_rate"]), I(r["play"]))).ToArray();

            var priority = (d["skill_masteries"] as JsonArray)?.FirstOrDefault()?["ids"] is JsonArray pr
                ? pr.Select(x => x!.GetValue<string>()).ToArray() : [];
            var skills = (d["skills"] as JsonArray ?? []).Select(s => new SkillOrder(
                s!["order"]!.AsArray().Select(x => x!.GetValue<string>()).ToArray(), priority, Wr(s), D(s["pick_rate"]), I(s["play"]))).ToArray();

            var counters = (d["counters"] as JsonArray ?? []).Select(c => new Matchup(I(c!["champion_id"]), I(c["play"]), Wr(c))).ToArray();

            static double? Place(JsonNode? x) { var p = I(x?["play"]); var t = D(x?["total_place"]); return p > 0 && t > 0 ? t / p : null; }
            var augments = (d["augment_group"] as JsonArray ?? []).Select(g => new AugmentGroup(I(g!["rarity"]),
                (g["augments"] as JsonArray ?? []).Select(a => new AugmentStat(I(a!["id"]), a["win_rate"] != null ? D(a["win_rate"]) : Wr(a),
                    D(a["pick_rate"]), I(a["play"]), Place(a))).ToArray())).ToArray();
            var synergies = (d["synergies"] as JsonArray ?? []).Select(s => new Synergy(I(s!["champion_id"]), I(s["play"]), Wr(s), Place(s))).ToArray();
            var play = I(posStats?["play"]);

            return new ChampionBuild(championId, position, Patch, posStats?["win_rate"] != null ? D(posStats["win_rate"]) : Wr(posStats),
                D(posStats?["pick_rate"]), D(posStats?["ban_rate"]),
                I(posStats?["tier_data"]?["tier"] ?? posStats?["tier"]), I(posStats?["tier_data"]?["rank"] ?? posStats?["rank"]), play,
                Sets(d["starter_items"]), Sets(d["core_items"]), Sets(d["boots"]), Sets(d["last_items"]), Sets(d["summoner_spells"]),
                runes, skills, counters)
            {
                Mode = mode, Augments = augments, Prismatic = Sets(d["prism_items"]), Synergies = synergies,
                AvgPlace = Place(posStats), FirstRate = play > 0 && posStats?["first_place"] != null ? D(posStats["first_place"]) / play : null,
            };
        }
    }

    static double D(JsonNode? n) { try { return n?.GetValue<double>() ?? 0; } catch { return 0; } }
    static int I(JsonNode? n) { try { return n == null ? 0 : (int)n.GetValue<double>(); } catch { return 0; } }
    static int[] Ints(JsonNode? n) => n is JsonArray a ? a.Select(x => I(x)).ToArray() : [];
    static double Wr(JsonNode? x) { var p = I(x?["play"]); return p > 0 ? (double)I(x?["win"]) / p : 0; }

    public void Dispose() => web.Dispose();
}
