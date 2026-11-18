using System.Collections.Concurrent;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace AnalisadorLoL;

/// <summary>
/// Laço principal. Pensado para gastar o mínimo durante a partida:
/// - seleção de campeões: 1 consulta/s (só enquanto a janela está visível);
/// - partida com a janela visível: 1 leitura a cada 3 s;
/// - partida com a janela minimizada: nenhuma leitura da partida, só checa a fase a cada 10 s.
/// </summary>
public sealed class Engine : IDisposable
{
    readonly MetaService meta;
    readonly LeagueClient lcu;
    readonly LiveClient live = new();
    readonly Analyzer analyzer;
    readonly CancellationTokenSource cts = new();
    readonly SemaphoreSlim wake = new(0);

    public AppSettings Settings { get; private set; }
    volatile bool visible = true;
    public bool Visible { get => visible; set { visible = value; if (value) Wake(); } }
    public event Action<string>? StateChanged;
    public event Action<string>? PhaseChanged;
    /// <summary>Avisos curtos para a tela (ex.: "runas importadas").</summary>
    public event Action<string>? Notified;
    public string LastJson { get; private set; } = "";

    string lastPhase = "";
    string currentMode = Mode.Classic;
    int arenaTeamSize;
    List<ArenaMate> arenaTeam = LoadArenaTeam();
    readonly ConcurrentDictionary<string, string> puuidName = new();
    List<(string Puuid, int ChampionId)> arenaOrder = [];
    long arenaOrderGame;

    /// <summary>Arena: puuid e campeão de cada jogador, na ordem da lista da partida (lido uma vez por partida).</summary>
    async Task<List<(string, int)>> ArenaOrderAsync()
    {
        if (arenaOrder.Count > 0 && arenaOrderGame != 0) return arenaOrder;
        var gd = (await lcu.GetAsync("/lol-gameflow/v1/session"))?["gameData"];
        var list = (gd?["playerChampionSelections"] as JsonArray ?? [])
            .Select(p => (p?["puuid"]?.GetValue<string>() ?? "", p?["championId"]?.GetValue<int>() ?? 0)).ToList();
        if (list.Count > 0) { arenaOrder = list; arenaOrderGame = gd?["gameId"]?.GetValue<long>() ?? 1; }
        return list;
    }
    static readonly string ArenaFile = Path.Combine(Paths.Local, "time-da-arena.json");

    static List<ArenaMate> LoadArenaTeam()
    {
        try
        {
            if (!File.Exists(ArenaFile) || DateTime.Now - File.GetLastWriteTime(ArenaFile) > TimeSpan.FromHours(2)) return [];
            return JsonSerializer.Deserialize<List<ArenaMate>>(File.ReadAllText(ArenaFile), Json.Opts) ?? [];
        }
        catch { return []; }
    }

    void SaveArenaTeam(List<ArenaMate> team)
    {
        if (team.Count == arenaTeam.Count && team.All(arenaTeam.Contains)) return;
        arenaTeam = team;
        try { Directory.CreateDirectory(Paths.Local); File.WriteAllText(ArenaFile, JsonSerializer.Serialize(team, Json.Opts)); } catch { }
    }

    /// <summary>Completa o nome dos companheiros (o cliente às vezes não manda na seleção).</summary>
    async Task<List<ArenaMate>> ArenaTeamWithNames()
    {
        var list = new List<ArenaMate>();
        foreach (var m in arenaTeam)
        {
            var name = m.Name;
            if (string.IsNullOrEmpty(name) && !puuidName.TryGetValue(m.Puuid, out name))
            {
                var s = await lcu.GetAsync($"/lol-summoner/v2/summoners/puuid/{m.Puuid}");
                name = s?["gameName"]?.GetValue<string>() ?? "";
                if (name.Length > 0) puuidName[m.Puuid] = name;
            }
            list.Add(m with { Name = name });
        }
        return list;
    }
    string? manualPosition;
    string autoRunesKey = "", autoSetKey = "", autoSpellsKey = "";
    int spellTries;
    (int Champ, string Pos, string Region, string Tier)? shopBuild;
    List<Slot> lastEnemies = [];
    GameView? lastGame;
    Dictionary<int, string> knownPositions = [];
    HashSet<int>? owned;
    Dictionary<int, int> mastery = [];
    DateTime playerDataAt = DateTime.MinValue;
    string? summonerName;
    long summonerId;
    readonly ConcurrentDictionary<string, RankInfo?> ranks = new();       // puuid → elo (null = buscando)
    readonly ConcurrentDictionary<string, string?> riotIdToPuuid = new();

    // Grupos (duo, trio…): exatos pelo cliente; estimados pelo histórico quando o cliente não informa
    Dictionary<string, PartyTag> exactByPuuid = [], guessedByPuuid = [];
    Dictionary<int, PartyTag> exactByChampion = [], guessedByChampion = [];
    DateTime partyAt = DateTime.MinValue;
    volatile bool guessing;
    readonly ConcurrentDictionary<string, HashSet<string>> histories = new(); // puuid → "partida:time"

    public Engine(MetaService meta, AppSettings settings)
    {
        this.meta = meta;
        Settings = settings;
        lcu = new LeagueClient(() => Settings.LeaguePath);
        analyzer = new Analyzer(meta, () => Settings);
        shopBuild = LoadShopBuild();
    }

    // A build enviada para a loja é guardada em disco: se o app for reaberto no meio da partida, continua igual.
    static readonly string ShopFile = Path.Combine(Paths.Local, "build-na-loja.json");

    static (int Champ, string Pos, string Region, string Tier)? LoadShopBuild()
    {
        try
        {
            if (!File.Exists(ShopFile) || DateTime.Now - File.GetLastWriteTime(ShopFile) > TimeSpan.FromHours(3)) return null;
            var n = JsonNode.Parse(File.ReadAllText(ShopFile))!;
            return (n["champ"]!.GetValue<int>(), n["pos"]!.GetValue<string>(), n["region"]!.GetValue<string>(), n["tier"]!.GetValue<string>());
        }
        catch { return null; }
    }

    void SaveShopBuild()
    {
        if (shopBuild is not { } b) return;
        try
        {
            Directory.CreateDirectory(Paths.Local);
            File.WriteAllText(ShopFile, new JsonObject { ["champ"] = b.Champ, ["pos"] = b.Pos, ["region"] = b.Region, ["tier"] = b.Tier }.ToJsonString());
        }
        catch { }
    }

    public void Start() => Task.Run(Loop);
    public void Wake() { if (wake.CurrentCount == 0) wake.Release(); }

    public void UpdateSettings(AppSettings s)
    {
        Settings = s;
        SettingsStore.Save(s);
        Wake();
    }

    public void SetManualPosition(string? pos) { manualPosition = Pos.From(pos); Wake(); }

    async Task Loop()
    {
        while (!cts.IsCancellationRequested)
        {
            int delay;
            try { delay = await Tick(); }
            catch (Exception ex) { Log.Write("Tick: " + ex); delay = 3000; }
            try { await wake.WaitAsync(delay, cts.Token); } catch (OperationCanceledException) { return; }
        }
    }

    async Task<int> Tick()
    {
        var s = Settings;
        var state = new AppState { Patch = meta.Patch };

        if (DemoMode.EndsWith("selecao"))
        {
            var dm = Demo.ModeOf(DemoMode);
            state.Mode = dm; state.ModeName = Mode.Name(dm);
            state.Client = new ClientInfo { Connected = true, Phase = "ChampSelect", Summoner = "Demonstração" };
            state.ChampSelect = await analyzer.ChampSelectAsync(new ChampSelectInput { Session = Demo.Session(dm), Mastery = Demo.Mastery, ManualPosition = manualPosition, Mode = dm });
            ApplyParties(state.ChampSelect.Allies, Demo.Parties(state.ChampSelect.Allies));
            Publish(state);
            return 3000;
        }
        if (DemoMode.EndsWith("partida"))
        {
            var dm = Demo.ModeOf(DemoMode);
            state.Mode = dm; state.ModeName = Mode.Name(dm);
            state.Client = new ClientInfo { Connected = true, Phase = "InProgress", Summoner = "Demonstração", LiveGame = true };
            state.Game = await analyzer.GameAsync(new GameInput { Data = Demo.Game(dm), Mode = dm,
                ArenaTeam = dm == Mode.Arena ? [new ArenaMate("demo", "Aliado1", 254)] : [] });
            foreach (var p in state.Game.Allies.Concat(state.Game.Enemies)) p.Party = Demo.GameParties.GetValueOrDefault(p.ChampionId);
            Publish(state);
            return 5000;
        }

        var connected = lcu.EnsureCredentials();
        var phase = "Desconectado";
        if (connected)
        {
            var ph = await lcu.GetAsync("/lol-gameflow/v1/gameflow-phase");
            connected = ph != null;
            phase = ph?.GetValue<string>() ?? "Desconectado";
        }
        if (!connected) { summonerName = null; summonerId = 0; }
        else if (summonerName == null)
        {
            var me = await lcu.GetAsync("/lol-summoner/v1/current-summoner");
            summonerName = me?["gameName"]?.GetValue<string>();
            summonerId = me?["summonerId"]?.GetValue<long>() ?? 0;
        }
        state.Client = new ClientInfo { Connected = connected, Phase = phase, Summoner = summonerName };
        state.Mode = currentMode;
        state.ModeName = Mode.Name(currentMode);

        if (phase != lastPhase)
        {
            profileAt = DateTime.MinValue; // fim de partida etc.: perfil e histórico atualizados
            if (phase == "ChampSelect") { manualPosition = null; autoRunesKey = autoSetKey = autoSpellsKey = ""; spellTries = 0; knownPositions = []; ResetParties(); arenaOrder = []; arenaOrderGame = 0; }
            if (phase is "ChampSelect" or "GameStart" or "InProgress" or "Reconnect") await RefreshMode();
            else if (phase is "None" or "Lobby" or "Desconectado") currentMode = Mode.Classic;
            else lastEnemies = [];
            if (phase is "None" or "Lobby" or "Matchmaking" or "Desconectado") ResetParties();
            lastPhase = phase;
            PhaseChanged?.Invoke(phase);
        }

        int delay;
        if (phase == "ChampSelect")
        {
            var session = await lcu.GetAsync("/lol-champ-select/v1/session");
            if (session != null)
            {
                await RefreshPlayerData();
                foreach (var p in session["myTeam"] as JsonArray ?? [])
                    if (p?["puuid"]?.GetValue<string>() is { Length: > 0 } puuid) QueueRank(puuid);

                var view = await analyzer.ChampSelectAsync(new ChampSelectInput
                {
                    Session = session, Owned = owned, Mastery = mastery, ManualPosition = manualPosition, Mode = currentMode,
                    RanksByPuuid = ranks.Where(r => r.Value != null).ToDictionary(r => r.Key, r => r.Value!),
                });
                await RefreshParties(view.Allies.Where(a => a.Puuid != null).Select(a => a.Puuid!).ToHashSet(), null);
                foreach (var a in view.Allies)
                    a.Party = a.Puuid == null ? null : exactByPuuid.GetValueOrDefault(a.Puuid) ?? guessedByPuuid.GetValueOrDefault(a.Puuid);
                foreach (var x in view.Allies.Concat(view.Enemies))
                    if (x.ChampionId > 0 && x.Position != null) knownPositions[x.ChampionId] = x.Position;
                state.ChampSelect = view;
                lastEnemies = view.Enemies;
                if (currentMode == Mode.Arena)
                    SaveArenaTeam(view.Allies.Where(a => !a.IsMe && a.Puuid != null).Select(a => new ArenaMate(a.Puuid!, a.Name, a.ChampionId)).ToList());
                await AutoImport(view);
            }
            delay = Visible ? 1000 : 3000;
        }
        else if (phase is "InProgress" or "GameStart" or "Reconnect" || !connected)
        {
            // Janela minimizada durante a partida: não lê nada da partida (custo ~zero).
            if (Visible)
            {
                var data = await live.GetAllAsync();
                if (data != null)
                {
                    state.Client.LiveGame = true;
                    if (connected) QueueGameRanks(data);
                    // Modo: o da fila (cliente) ou, sem cliente, o informado pela própria partida
                    var liveMode = Mode.FromGameMode(data["gameData"]?["gameMode"]?.GetValue<string>());
                    var gameMode = currentMode == Mode.Mayhem && liveMode == Mode.Aram ? Mode.Mayhem : liveMode != Mode.Classic ? liveMode : currentMode;
                    state.Mode = gameMode; state.ModeName = Mode.Name(gameMode);
                    lastGame = await analyzer.GameAsync(new GameInput
                    {
                        Data = data, KnownPositions = knownPositions, ShopBuild = shopBuild, Mode = gameMode, ArenaTeamSize = arenaTeamSize,
                        ArenaTeam = gameMode == Mode.Arena && connected ? await ArenaTeamWithNames() : arenaTeam,
                        ArenaOrder = gameMode == Mode.Arena && connected ? await ArenaOrderAsync() : [],
                        RanksByRiotId = riotIdToPuuid.Where(kv => kv.Value != null && ranks.TryGetValue(kv.Value, out var r) && r != null)
                            .ToDictionary(kv => kv.Key, kv => ranks[kv.Value!]!),
                    });
                    if (connected)
                    {
                        var players = lastGame.Allies.Concat(lastGame.Enemies).ToList();
                        await RefreshParties(null, players.Select(p => p.ChampionId).Where(id => id > 0).ToHashSet());
                        // Arena: campeões podem repetir, então o grupo é casado pelo jogador (puuid), não pelo campeão
                        var byIndex = gameMode == Mode.Arena && arenaOrder.Count == players.Count;
                        foreach (var p in players)
                            p.Party = byIndex
                                ? exactByPuuid.GetValueOrDefault(arenaOrder[p.Index].Puuid) ?? guessedByPuuid.GetValueOrDefault(arenaOrder[p.Index].Puuid)
                                : exactByChampion.GetValueOrDefault(p.ChampionId) ?? guessedByChampion.GetValueOrDefault(p.ChampionId);
                        // Arena sem o time da seleção: quem está em grupo pré-formado com você está no seu time
                        if (gameMode == Mode.Arena && !lastGame.ArenaTeamKnown && lastGame.Me?.Party is { Exact: true } myParty)
                        {
                            var mates = lastGame.Enemies.Where(p => p.Party is { Exact: true } pp && pp.Group == myParty.Group).ToList();
                            if (mates.Count > 0)
                            {
                                lastGame.Allies.AddRange(mates);
                                lastGame.Enemies.RemoveAll(mates.Contains);
                                lastGame.ArenaTeamKnown = true;
                                lastGame.AllyGold = lastGame.Allies.Sum(p => p.ItemGold);
                                lastGame.EnemyGold = lastGame.Enemies.Sum(p => p.ItemGold);
                                lastGame.AllyObjectives.Kills = lastGame.Allies.Sum(p => p.Kills);
                                lastGame.EnemyObjectives.Kills = lastGame.Enemies.Sum(p => p.Kills);
                            }
                        }
                    }
                }
                else if (!connected) lastGame = null;
            }
            state.Game = lastGame;
            state.Client.LiveGame = lastGame != null;
            delay = !Visible ? 10000 : state.Game != null ? 3000 : 5000;
        }
        else
        {
            lastGame = null;
            state.Profile = connected ? await RefreshProfile() : profile;
            if (phase != "ReadyCheck") readyAccepted = false;
            else if (s.AutoAccept) await AutoAccept();
            // Com o aceite automático ligado, a fila é checada a cada 2 s mesmo minimizado (a janela de aceite dura ~10 s).
            delay = Visible || (s.AutoAccept && phase is "Matchmaking" or "ReadyCheck") ? 2000 : 5000;
        }
        state.Profile ??= profile; // a aba Perfil funciona também durante a seleção e a partida
        Publish(state);
        return delay;
    }

    bool readyAccepted;

    // ------------------------------------------------------------------ perfil (tela inicial)
    ProfileView? profile = LoadProfile();
    DateTime profileAt = DateTime.MinValue;
    static readonly string ProfileFile = Path.Combine(Paths.Local, "perfil.json");

    // O último perfil fica guardado: com o cliente fechado, a tela inicial continua mostrando os seus dados
    static ProfileView? LoadProfile()
    {
        try { return File.Exists(ProfileFile) ? JsonSerializer.Deserialize<ProfileView>(File.ReadAllText(ProfileFile), Json.Opts) : null; }
        catch { return null; }
    }

    async Task<ProfileView?> RefreshProfile()
    {
        if (profile != null && DateTime.UtcNow - profileAt < TimeSpan.FromMinutes(3)) return profile;
        profileAt = DateTime.UtcNow;
        var meTask = lcu.GetAsync("/lol-summoner/v1/current-summoner");
        var rankTask = lcu.GetAsync("/lol-ranked/v1/current-ranked-stats");
        var mastTask = lcu.GetAsync("/lol-champion-mastery/v1/local-player/champion-mastery");
        var histTask = lcu.GetAsync("/lol-match-history/v1/products/lol/current-summoner/matches?begIndex=0&endIndex=20");
        await Task.WhenAll(meTask, rankTask, mastTask, histTask);
        var me = meTask.Result;
        if (me == null) return profile;

        var p = new ProfileView
        {
            Name = me["gameName"]?.GetValue<string>() ?? "", Tag = me["tagLine"]?.GetValue<string>() ?? "",
            Level = me["summonerLevel"]?.GetValue<int>() ?? 0, LevelPct = me["percentCompleteForNextLevel"]?.GetValue<int>() ?? 0,
            Icon = $"https://ddragon.leagueoflegends.com/cdn/{meta.Version}/img/profileicon/{me["profileIconId"]?.GetValue<int>() ?? 0}.png",
        };
        foreach (var q in rankTask.Result?["queues"] as JsonArray ?? [])
        {
            var type = q?["queueType"]?.GetValue<string>();
            if (type is not ("RANKED_SOLO_5x5" or "RANKED_FLEX_SR")) continue;
            string S(string k) { var v = q![k]?.GetValue<string>() ?? ""; return v is "NA" or "NONE" ? "" : v; }
            p.Ranks.Add(new RankedQueue(type, S("tier"), S("division"), q!["leaguePoints"]?.GetValue<int>() ?? 0,
                q["wins"]?.GetValue<int>() ?? 0, q["losses"]?.GetValue<int>() ?? 0, S("highestTier"), S("highestDivision")));
        }
        p.Mastery = (mastTask.Result as JsonArray ?? []).Where(m => m != null)
            .Select(m => new MasteryEntry(m!["championId"]!.GetValue<int>(), m["championLevel"]?.GetValue<int>() ?? 0, (int)(m["championPoints"]?.GetValue<long>() ?? 0)))
            .OrderByDescending(m => m.Points).Take(6).ToList();
        foreach (var g in histTask.Result?["games"]?["games"] as JsonArray ?? [])
        {
            var pa = (g?["participants"] as JsonArray)?.FirstOrDefault();
            var st = pa?["stats"];
            if (g == null || st == null) continue;
            int I(string k) => st[k]?.GetValue<int>() ?? 0;
            p.Matches.Add(new MatchEntry(g["gameId"]?.GetValue<long>() ?? 0, g["queueId"]?.GetValue<int>() ?? 0, g["gameMode"]?.GetValue<string>() ?? "",
                pa!["championId"]?.GetValue<int>() ?? 0, st["win"]?.GetValue<bool>() ?? false, I("kills"), I("deaths"), I("assists"),
                I("totalMinionsKilled") + I("neutralMinionsKilled"), I("goldEarned"), g["gameDuration"]?.GetValue<int>() ?? 0,
                g["gameCreation"]?.GetValue<long>() ?? 0,
                Enumerable.Range(0, 7).Select(i => I($"item{i}")).ToArray(), I("subteamPlacement"),
                Enumerable.Range(1, 6).Select(i => I($"playerAugment{i}")).Where(a => a > 0).ToArray(),
                st["gameEndedInEarlySurrender"]?.GetValue<bool>() == true));
        }
        p.Matches = p.Matches.OrderByDescending(m => m.Created).ToList();
        p.UpdatedAt = DateTime.Now;
        profile = p;
        try { Directory.CreateDirectory(Paths.Local); File.WriteAllText(ProfileFile, JsonSerializer.Serialize(p, Json.Opts)); } catch { }
        return p;
    }

    /// <summary>Lê a fila atual (ranqueada, ARAM, Arena...) na sessão do cliente.</summary>
    async Task RefreshMode()
    {
        var q = (await lcu.GetAsync("/lol-gameflow/v1/session"))?["gameData"]?["queue"];
        if (q == null) return;
        var mode = Mode.FromQueue(q["gameMode"]?.GetValue<string>(), q["id"]?.GetValue<int>() ?? 0, q["description"]?.GetValue<string>() ?? q["name"]?.GetValue<string>());
        if (mode != currentMode) Log.Write($"Modo: {Mode.Name(mode)} (fila {q["id"]}, {q["gameMode"]}).");
        currentMode = mode;
        // "Arena 3x6" = trios; a Arena comum é de duplas
        var m = System.Text.RegularExpressions.Regex.Match(q["description"]?.GetValue<string>() ?? "", @"(\d)\s*x\s*(\d)");
        arenaTeamSize = m.Success ? int.Parse(m.Groups[1].Value) : 0;
    }

    // ------------------------------------------------------------------ grupos (duo, trio…)

    void ResetParties()
    {
        exactByPuuid = []; guessedByPuuid = []; exactByChampion = []; guessedByChampion = [];
        partyAt = DateTime.MinValue;
        histories.Clear();
    }

    static void ApplyParties(List<Slot> slots, Dictionary<int, PartyTag> byCell)
    {
        foreach (var s in slots) s.Party = byCell.GetValueOrDefault(s.CellId);
    }

    /// <summary>
    /// Lê os grupos da sessão da partida (campo teamParticipantId: quem está no mesmo grupo tem o mesmo número).
    /// A sessão guarda a partida anterior até a próxima começar, por isso cada time só é aceito
    /// se bater com os jogadores (seleção) ou campeões (partida) atuais.
    /// </summary>
    async Task RefreshParties(HashSet<string>? allyPuuids, HashSet<int>? liveChamps)
    {
        if (DateTime.UtcNow - partyAt < TimeSpan.FromSeconds(15)) return;
        partyAt = DateTime.UtcNow;
        var gd = (await lcu.GetAsync("/lol-gameflow/v1/session"))?["gameData"];
        if (gd == null) return;
        var currentGame = gd["gameId"]?.GetValue<long>() ?? 0;

        var byPuuid = new Dictionary<string, PartyTag>();
        var byChamp = new Dictionary<int, PartyTag>();
        var toGuess = new List<List<(string Puuid, int Champ)>>();
        foreach (var teamNode in new[] { gd["teamOne"], gd["teamTwo"] })
        {
            var members = (teamNode as JsonArray ?? []).Where(m => m != null).Select(m => (
                Puuid: m!["puuid"]?.GetValue<string>() ?? "",
                Champ: m["championId"]?.GetValue<int>() ?? 0,
                Party: m["teamParticipantId"]?.GetValue<long>() ?? 0)).ToList();
            if (members.Count < 2) continue;
            if (allyPuuids != null && (allyPuuids.Count < 2 || !members.All(m => allyPuuids.Contains(m.Puuid)))) continue;
            if (liveChamps != null && !members.All(m => liveChamps.Contains(m.Champ))) continue;

            if (members.Any(m => m.Party > 0))
            {
                int n = 0;
                foreach (var g in members.Where(m => m.Party > 0).GroupBy(m => m.Party).Where(g => g.Count() >= 2))
                {
                    n++;
                    foreach (var m in g)
                    {
                        var tag = new PartyTag(n, g.Count(), true, 0);
                        if (m.Puuid != "") byPuuid[m.Puuid] = tag;
                        if (m.Champ > 0) byChamp[m.Champ] = tag;
                    }
                }
            }
            else if (members.Count(m => m.Puuid != "") >= 2)
                toGuess.Add(members.Where(m => m.Puuid != "").Select(m => (m.Puuid, m.Champ)).ToList());
        }
        exactByPuuid = byPuuid;
        exactByChampion = byChamp;
        if (toGuess.Count > 0 && !guessing)
        {
            guessing = true;
            _ = Task.Run(() => GuessParties(toGuess, currentGame));
        }
    }

    /// <summary>
    /// Plano B: quem jogou junto no mesmo time em 2 ou mais das últimas 20 partidas é tratado como grupo provável.
    /// Uma consulta por jogador, feita uma vez por partida.
    /// </summary>
    async Task GuessParties(List<List<(string Puuid, int Champ)>> teams, long currentGame)
    {
        try
        {
            await Task.WhenAll(teams.SelectMany(t => t).Select(async m =>
            {
                if (histories.ContainsKey(m.Puuid)) return;
                var h = await lcu.GetAsync($"/lol-match-history/v1/products/lol/{m.Puuid}/matches?begIndex=0&endIndex=20");
                var set = new HashSet<string>();
                foreach (var g in h?["games"]?["games"] as JsonArray ?? [])
                {
                    var gid = g?["gameId"]?.GetValue<long>() ?? 0;
                    if (gid == 0 || gid == currentGame) continue;
                    var team = (g!["participants"] as JsonArray)?.FirstOrDefault()?["teamId"]?.GetValue<int>() ?? 0;
                    set.Add($"{gid}:{team}");
                }
                histories[m.Puuid] = set;
            }));

            var byPuuid = new Dictionary<string, PartyTag>();
            var byChamp = new Dictionary<int, PartyTag>();
            foreach (var team in teams)
            {
                var parent = Enumerable.Range(0, team.Count).ToArray();
                int Find(int x) => parent[x] == x ? x : (parent[x] = Find(parent[x]));
                var together = new Dictionary<(int, int), int>();
                for (int i = 0; i < team.Count; i++)
                    for (int j = i + 1; j < team.Count; j++)
                    {
                        if (!histories.TryGetValue(team[i].Puuid, out var a) || !histories.TryGetValue(team[j].Puuid, out var b)) continue;
                        var shared = a.Count(b.Contains);
                        if (shared < 2) continue;
                        parent[Find(i)] = Find(j);
                        together[(i, j)] = shared;
                    }
                int n = 0;
                foreach (var g in Enumerable.Range(0, team.Count).GroupBy(Find).Where(g => g.Count() >= 2))
                {
                    n++;
                    var games = together.Where(kv => g.Contains(kv.Key.Item1) && g.Contains(kv.Key.Item2)).Select(kv => kv.Value).DefaultIfEmpty(0).Max();
                    foreach (var i in g)
                    {
                        var tag = new PartyTag(n, g.Count(), false, games);
                        byPuuid[team[i].Puuid] = tag;
                        if (team[i].Champ > 0) byChamp[team[i].Champ] = tag;
                    }
                }
            }
            guessedByPuuid = byPuuid;
            guessedByChampion = byChamp;
            Wake();
        }
        catch (Exception ex) { Log.Write("Grupos: " + ex.Message); }
        finally { guessing = false; }
    }

    /// <summary>Aceita a partida encontrada. Não faz nada se você já respondeu (aceitou ou recusou) na mão.</summary>
    async Task AutoAccept()
    {
        if (readyAccepted) return;
        var rc = await lcu.GetAsync("/lol-matchmaking/v1/ready-check");
        if (rc?["state"]?.GetValue<string>() != "InProgress" || rc["playerResponse"]?.GetValue<string>() != "None") return;
        using var res = await lcu.SendAsync(HttpMethod.Post, "/lol-matchmaking/v1/ready-check/accept");
        if (res?.IsSuccessStatusCode != true) return;
        readyAccepted = true;
        Log.Write("Partida aceita automaticamente.");
        Notified?.Invoke("Partida aceita automaticamente.");
    }

    void Publish(AppState state)
    {
        var json = JsonSerializer.Serialize(state, Json.Opts);
        if (json == LastJson) return;
        LastJson = json;
        if (Visible) StateChanged?.Invoke(json);
    }

    // ------------------------------------------------------------------ dados do jogador

    async Task RefreshPlayerData()
    {
        if (DateTime.UtcNow - playerDataAt < TimeSpan.FromMinutes(5)) return;
        playerDataAt = DateTime.UtcNow;
        var ownedTask = lcu.GetAsync("/lol-champions/v1/owned-champions-minimal");
        var masteryTask = lcu.GetAsync("/lol-champion-mastery/v1/local-player/champion-mastery");
        if (await ownedTask is JsonArray oa)
            owned = oa.Where(c => c?["ownership"]?["owned"]?.GetValue<bool>() == true || c?["freeToPlay"]?.GetValue<bool>() == true)
                .Select(c => c!["id"]!.GetValue<int>()).ToHashSet();
        if (await masteryTask is JsonArray ma)
            mastery = ma.Where(m => m != null).GroupBy(m => m!["championId"]!.GetValue<int>())
                .ToDictionary(g => g.Key, g => g.Max(m => (int)m!["championPoints"]!.GetValue<long>()));
    }

    void QueueRank(string puuid)
    {
        if (!ranks.TryAdd(puuid, null)) return;
        _ = Task.Run(async () =>
        {
            var r = await lcu.GetAsync($"/lol-ranked/v1/ranked-stats/{puuid}");
            var solo = r?["queueMap"]?["RANKED_SOLO_5x5"];
            var tier = solo?["tier"]?.GetValue<string>() ?? "";
            ranks[puuid] = new RankInfo(tier is "" or "NONE" ? "SEM ELO" : tier, solo?["division"]?.GetValue<string>() is "NA" or null ? "" : solo["division"]!.GetValue<string>(),
                solo?["leaguePoints"]?.GetValue<int>() ?? 0, solo?["wins"]?.GetValue<int>() ?? 0, solo?["losses"]?.GetValue<int>() ?? 0);
        });
    }

    void QueueGameRanks(JsonNode data)
    {
        foreach (var p in data["allPlayers"] as JsonArray ?? [])
        {
            var riotId = p?["riotId"]?.GetValue<string>();
            if (string.IsNullOrEmpty(riotId) || !riotId.Contains('#') || p?["isBot"]?.GetValue<bool>() == true) continue;
            if (!riotIdToPuuid.TryAdd(riotId, null)) continue;
            _ = Task.Run(async () =>
            {
                var parts = riotId.Split('#', 2);
                var r = await lcu.GetAsync($"/lol-summoner/v1/alias/lookup?gameName={Uri.EscapeDataString(parts[0])}&tagLine={Uri.EscapeDataString(parts[1])}");
                var puuid = r?["puuid"]?.GetValue<string>();
                riotIdToPuuid[riotId] = puuid;
                if (puuid != null) QueueRank(puuid);
            });
        }
    }

    // ------------------------------------------------------------------ ações no cliente

    async Task AutoImport(ChampSelectView v)
    {
        // Dispara quando o campeão está confirmado (ou na fase final, por causa de trocas entre aliados).
        var pos = v.Mode == Mode.Classic ? v.MyPosition : v.Mode.ToUpperInvariant();
        if (v.MyChampionId == 0 || pos == null || !(v.MyLocked || v.Final)) return;
        var key = $"{v.MyChampionId}-{pos}-{Settings.Region}-{Settings.Tier}";
        var notes = new List<string>();
        if (Settings.AutoRunes && key != autoRunesKey && Mode.HasRunes(v.Mode))
        {
            autoRunesKey = key;
            var r = await ImportRunes(v.MyChampionId, pos, 0);
            if (r.StartsWith("Sem dados")) autoRunesKey = ""; // dados ainda não chegaram: tenta de novo
            else notes.Add(r);
        }
        // Feitiços de invocador acompanham as runas (todos os modos, menos a Arena, que não tem feitiços).
        if (Settings.AutoRunes && key != autoSpellsKey && v.Mode != Mode.Arena)
        {
            autoSpellsKey = key;
            var r = await ApplySpells(v.MyChampionId, pos, 0);
            if (r.StartsWith("Sem dados") || (!r.StartsWith("Feitiços") && ++spellTries < 3)) autoSpellsKey = ""; // tenta de novo
            else { spellTries = 0; notes.Add(r); }
        }
        // A build é reenviada sempre que um inimigo novo aparece: o bloco de situacionais acompanha o time adversário.
        var setKey = key + "|" + string.Join(",", v.Enemies.Where(e => e.ChampionId > 0).Select(e => e.ChampionId).Order());
        if (Settings.AutoItemSet && setKey != autoSetKey)
        {
            var first = !autoSetKey.StartsWith(key + "|");
            autoSetKey = setKey;
            var msg = await ImportItemSet(v.MyChampionId, pos);
            if (msg.StartsWith("Sem dados")) autoSetKey = ""; // tenta de novo no próximo ciclo
            else if (first) notes.Add(msg);
        }
        if (notes.Count > 0)
        {
            Log.Write("Automático: " + string.Join(" ", notes));
            Notified?.Invoke(string.Join(" ", notes));
        }
    }

    string ChampName(int id) => meta.Champions.TryGetValue(id, out var c) ? c.Name : "Campeão";
    static string PosName(string pos) => ModeOfPos(pos) is { } m ? Mode.Name(m)
        : pos switch { Pos.Top => "Topo", Pos.Jungle => "Selva", Pos.Mid => "Meio", Pos.Adc => "Atirador", _ => "Suporte" };

    /// <summary>Nos modos sem rota, a "rota" é o nome do modo (ARAM, ARENA...).</summary>
    public static string? ModeOfPos(string? pos) => pos?.ToUpperInvariant() switch
    {
        "ARAM" => Mode.Aram, "MAYHEM" => Mode.Mayhem, "ARENA" => Mode.Arena, "URF" => Mode.Urf, "NEXUS" => Mode.Nexus, _ => null,
    };
    public static string NormalizePos(string? raw) => ModeOfPos(raw) != null ? raw!.ToUpperInvariant() : Pos.From(raw) ?? Pos.Top;

    Task<ChampionBuild?> BuildFor(int championId, string pos) => ModeOfPos(pos) is { } m
        ? meta.GetModeBuildAsync(championId, m, Settings.Region)
        : meta.GetBuildAsync(championId, pos, Settings.Region, Settings.Tier);

    /// <summary>ARAM: pega um campeão do banco de trocas.</summary>
    public async Task<string> SwapBench(int championId)
    {
        if (DemoActive) return "No modo demonstração nada é enviado ao cliente.";
        using var res = await lcu.SendAsync(HttpMethod.Post, $"/lol-champ-select/v1/session/bench/swap/{championId}");
        return res?.IsSuccessStatusCode == true ? $"{ChampName(championId)} pego do banco." : "Não foi possível trocar (o banco pode estar em espera).";
    }

    /// <summary>Só para testes de desenvolvimento (variável de ambiente); não aparece para o usuário.</summary>
    static readonly string DemoMode = Environment.GetEnvironmentVariable("ANALISADOR_DEMO") ?? "";
    bool DemoActive => DemoMode != "";
    static string TierName(string t) => t switch
    {
        "all" => "todos", "gold_plus" => "Ouro+", "platinum_plus" => "Plat+", "emerald_plus" => "Esm+",
        "diamond_plus" => "Diam+", "master_plus" => "Mestre+", "challenger" => "Desaf.", _ => t,
    };

    public async Task<string> ImportRunes(int championId, string position, int index)
    {
        if (DemoActive) return "No modo demonstração nada é enviado ao cliente.";
        if (ModeOfPos(position) == Mode.Arena) return "A Arena não usa runas.";
        var build = await BuildFor(championId, position);
        var page = build?.Runes.ElementAtOrDefault(index);
        if (page == null) return "Sem dados de runas para este campeão.";
        if (!lcu.EnsureCredentials()) return "Cliente do LoL não encontrado.";

        var pages = await lcu.GetAsync("/lol-perks/v1/pages") as JsonArray ?? [];
        // Remove a página criada antes pelo app
        foreach (var p in pages.Where(p => p?["name"]?.GetValue<string>()?.StartsWith("AL ") == true))
            (await lcu.SendAsync(HttpMethod.Delete, $"/lol-perks/v1/pages/{p!["id"]}"))?.Dispose();

        var inv = await lcu.GetAsync("/lol-perks/v1/inventory");
        if (inv?["canAddCustomPage"]?.GetValue<bool>() != true)
        {
            // Sem espaço: substitui a página atual (ou a primeira editável)
            pages = await lcu.GetAsync("/lol-perks/v1/pages") as JsonArray ?? [];
            var victim = pages.FirstOrDefault(p => p?["current"]?.GetValue<bool>() == true && p["isDeletable"]?.GetValue<bool>() == true)
                         ?? pages.FirstOrDefault(p => p?["isDeletable"]?.GetValue<bool>() == true);
            if (victim == null) return "Não há página de runas editável.";
            (await lcu.SendAsync(HttpMethod.Delete, $"/lol-perks/v1/pages/{victim["id"]}"))?.Dispose();
        }

        var body = new JsonObject
        {
            ["name"] = $"AL {ChampName(championId)} {PosName(position)}",
            ["primaryStyleId"] = page.PrimaryStyle,
            ["subStyleId"] = page.SubStyle,
            ["selectedPerkIds"] = new JsonArray(page.Primary.Concat(page.Secondary).Concat(page.Shards).Select(i => (JsonNode)i).ToArray()),
            ["current"] = true,
        };
        using var res = await lcu.SendAsync(HttpMethod.Post, "/lol-perks/v1/pages", body);
        return res?.IsSuccessStatusCode == true ? "Runas importadas para o cliente." : $"O cliente recusou as runas ({(int?)res?.StatusCode}).";
    }

    public async Task<string> ImportItemSet(int championId, string position)
    {
        if (DemoActive) return "No modo demonstração nada é enviado ao cliente.";
        var b = await BuildFor(championId, position);
        if (b == null) return "Sem dados de build para este campeão.";
        var mode = ModeOfPos(position) ?? Mode.Classic;
        if (!lcu.EnsureCredentials() || summonerId == 0) return "Cliente do LoL não encontrado.";

        var path = $"/lol-item-sets/v1/item-sets/{summonerId}/sets";
        var current = await lcu.GetAsync(path) as JsonObject ?? new JsonObject { ["accountId"] = summonerId, ["timestamp"] = 0 };
        var sets = current["itemSets"] as JsonArray ?? [];
        var keep = new JsonArray();
        foreach (var set in sets.ToList())
        {
            var mine = set?["title"]?.GetValue<string>()?.StartsWith("AL ") == true &&
                       (set["associatedChampions"] as JsonArray)?.Any(c => c?.GetValue<int>() == championId) == true;
            if (!mine && set != null) keep.Add(set.DeepClone());
        }

        static string Pct(double v) => (v * 100).ToString("0") + "%";
        JsonObject Block(string title, IEnumerable<int> ids) => new()
        {
            ["type"] = title,
            ["items"] = new JsonArray(ids.Where(meta.Items.ContainsKey).Distinct().Select(i => (JsonNode)new JsonObject { ["id"] = i.ToString(), ["count"] = 1 }).ToArray()),
        };
        var blocks = new JsonArray();
        if (b.Starters.FirstOrDefault() is { } st) blocks.Add(Block($"Iniciais ({Pct(st.WinRate)} vitórias)", mode == Mode.Classic ? st.Ids.Concat([3340, 2055]) : st.Ids));
        if (b.Core.FirstOrDefault() is { } core) blocks.Add(Block($"Build principal ({Pct(core.WinRate)} vitórias)", core.Ids));
        if (b.Prismatic.Length > 0) blocks.Add(Block("Itens prismáticos", b.Prismatic.Take(6).Select(x => x.Ids[0])));
        if (b.Boots.Length > 0) blocks.Add(Block("Botas", b.Boots.Take(3).Select(x => x.Ids[0])));
        blocks.Add(Block("Opções finais", b.Late.Select(x => x.Ids[0]).Where(i => b.Core.FirstOrDefault()?.Ids.Contains(i) != true && meta.Items.TryGetValue(i, out var it) && it.Into.Length == 0).Take(8)));
        // Situacionais contra o time inimigo já revelado na seleção
        var foes = lastEnemies.Where(e => e.ChampionId > 0).Select(e => new PlayerView { ChampionId = e.ChampionId, Position = e.Position }).ToList();
        if (foes.Count > 0)
        {
            var (_, sit) = Situational.Analyze(meta, new PlayerView { ChampionId = championId, Position = position }, foes, b, mode);
            if (sit.Count > 0) blocks.Add(Block("Situacionais contra este time", sit.Select(x => x.ItemId)));
        }
        if (mode == Mode.Classic || mode == Mode.Urf) blocks.Add(Block("Consumíveis", [2003, 2055, 2138, 2139, 2140]));
        else if (mode != Mode.Arena) blocks.Add(Block("Consumíveis", [2003, 2138, 2139, 2140]));

        keep.Add(new JsonObject
        {
            ["associatedChampions"] = new JsonArray(championId),
            ["associatedMaps"] = new JsonArray(Mode.MapId(mode)),
            ["blocks"] = blocks,
            ["map"] = "any", ["mode"] = "any",
            ["preferredItemSlots"] = new JsonArray(),
            ["sortrank"] = 0, ["startedFrom"] = "blank",
            ["title"] = mode == Mode.Classic ? $"AL {ChampName(championId)} {PosName(position)} ({meta.Patch} {TierName(Settings.Tier)})"
                                             : $"AL {ChampName(championId)} {PosName(position)} ({meta.Patch})",
            ["type"] = "custom", ["uid"] = Guid.NewGuid().ToString(),
        });
        current["itemSets"] = keep;
        using var res = await lcu.SendAsync(HttpMethod.Put, path, current);
        if (res?.IsSuccessStatusCode != true) return $"O cliente recusou a build ({(int?)res?.StatusCode}).";
        shopBuild = (championId, position, Settings.Region, Settings.Tier);
        SaveShopBuild();
        Log.Write($"Build na loja: campeão {championId} {position}, {Settings.Region}/{Settings.Tier}, núcleo {string.Join(",", b.Core.FirstOrDefault()?.Ids ?? [])}");
        return "Build enviada para a loja do jogo.";
    }

    public async Task<string> ApplySpells(int championId, string position, int index)
    {
        if (DemoActive) return "No modo demonstração nada é enviado ao cliente.";
        if (ModeOfPos(position) == Mode.Arena) return "A Arena não usa feitiços de invocador.";
        var b = await BuildFor(championId, position);
        var combo = b?.Spells.ElementAtOrDefault(index);
        if (combo == null || combo.Ids.Length < 2) return "Sem dados de feitiços.";
        int a = combo.Ids[0], c = combo.Ids[1];
        const int flash = 4;
        // Flash na tecla escolhida nas configurações
        if ((Settings.FlashKey == "D" && c == flash) || (Settings.FlashKey == "F" && a == flash)) (a, c) = (c, a);
        using var res = await lcu.SendAsync(new HttpMethod("PATCH"), "/lol-champ-select/v1/session/my-selection",
            new JsonObject { ["spell1Id"] = a, ["spell2Id"] = c });
        return res?.IsSuccessStatusCode == true ? "Feitiços aplicados." : "Só é possível trocar feitiços durante a seleção de campeões.";
    }

    public void Dispose()
    {
        cts.Cancel();
        lcu.Dispose();
        live.Dispose();
    }
}

public static class Log
{
    static readonly object Gate = new();
    public static void Write(string msg)
    {
        try
        {
            lock (Gate)
            {
                Directory.CreateDirectory(Paths.Local);
                var file = Path.Combine(Paths.Local, "registro.log");
                if (File.Exists(file) && new FileInfo(file).Length > 512_000) File.Delete(file);
                File.AppendAllText(file, $"{DateTime.Now:dd/MM/yyyy HH:mm:ss} {msg}{Environment.NewLine}");
            }
        }
        catch { }
    }
}
