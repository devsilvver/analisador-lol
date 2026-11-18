using System.Globalization;
using System.Text.Json.Nodes;

namespace AnalisadorLoL;

public sealed class ChampSelectInput
{
    public required JsonNode Session { get; init; }
    public string Mode { get; init; } = AnalisadorLoL.Mode.Classic;
    public HashSet<int>? Owned { get; init; }
    public Dictionary<int, int> Mastery { get; init; } = [];
    public string? ManualPosition { get; init; }
    public Dictionary<string, RankInfo> RanksByPuuid { get; init; } = [];
}

public sealed class GameInput
{
    public required JsonNode Data { get; init; }
    public string Mode { get; init; } = AnalisadorLoL.Mode.Classic;
    /// <summary>Arena: jogadores por time (2 na Arena normal, 3 na "Arena 3x6"). 0 = deduzir.</summary>
    public int ArenaTeamSize { get; init; }
    /// <summary>Arena: seu time, guardado na seleção de campeões (a API da partida não informa).</summary>
    public List<ArenaMate> ArenaTeam { get; init; } = [];
    /// <summary>Arena: identificador (puuid) e campeão de cada jogador, na mesma ordem da lista da partida (vem do cliente).</summary>
    public List<(string Puuid, int ChampionId)> ArenaOrder { get; init; } = [];
    /// <summary>Rotas lembradas da seleção de campeões (campeão → rota).</summary>
    public Dictionary<int, string> KnownPositions { get; init; } = [];
    public Dictionary<string, RankInfo> RanksByRiotId { get; init; } = [];
    /// <summary>Build enviada para a loja nesta partida (campeão, rota, região, elo).</summary>
    public (int Champ, string Pos, string Region, string Tier)? ShopBuild { get; init; }
}

public sealed class Analyzer(MetaService meta, Func<AppSettings> settings)
{
    static readonly CultureInfo Br = CultureInfo.GetCultureInfo("pt-BR");
    static string Pct(double v) => (v * 100).ToString("0.0", Br) + "%";
    static string Num(int v) => v.ToString("N0", Br);

    /// <summary>Taxa de vitória suavizada: amostras pequenas puxam para a média (evita 100% em 3 jogos).</summary>
    static double Smooth(double wr, int play, double prior, int k) => (wr * play + prior * k) / (play + k);

    string Name(int id) => meta.Champions.TryGetValue(id, out var c) ? c.Name : $"#{id}";

    static Dictionary<int, Dictionary<string, double>> RoleRates(IEnumerable<TierEntry> tier)
    {
        var d = new Dictionary<int, Dictionary<string, double>>();
        foreach (var e in tier)
        {
            if (!d.TryGetValue(e.ChampionId, out var m)) d[e.ChampionId] = m = [];
            m[e.Position] = e.RoleRate;
        }
        return d;
    }

    /// <summary>
    /// Distribui campeões nas rotas livres maximizando a chance conjunta (força bruta: no máximo 5! = 120 combinações).
    /// </summary>
    static Dictionary<int, (string pos, double conf)> AssignRoles(IList<int> champs, Dictionary<int, Dictionary<string, double>> rates,
        ICollection<string> taken, Dictionary<int, string>? forced = null)
    {
        var free = Pos.All.Where(p => !taken.Contains(p)).ToList();
        var result = new Dictionary<int, (string, double)>();
        var list = champs.Where(c => c > 0).Distinct().Take(free.Count).ToList();
        if (list.Count == 0) return result;

        double Rate(int champ, string pos)
        {
            if (forced != null && forced.TryGetValue(champ, out var f)) return f == pos ? 1 : 0.0001;
            return rates.TryGetValue(champ, out var m) && m.TryGetValue(pos, out var r) ? r : 0.0005;
        }

        double best = double.NegativeInfinity;
        string[] bestAssign = new string[list.Count], cur = new string[list.Count];
        var used = new bool[free.Count];
        void Search(int i, double score)
        {
            if (i == list.Count) { if (score > best) { best = score; cur.CopyTo(bestAssign, 0); } return; }
            for (int j = 0; j < free.Count; j++)
            {
                if (used[j]) continue;
                used[j] = true; cur[i] = free[j];
                Search(i + 1, score + Math.Log(Rate(list[i], free[j]) + 0.001));
                used[j] = false;
            }
        }
        Search(0, 0);
        for (int i = 0; i < list.Count; i++)
        {
            var rs = Pos.All.Sum(p => Rate(list[i], p));
            result[list[i]] = (bestAssign[i], rs > 0 ? Math.Min(1, Rate(list[i], bestAssign[i]) / rs) : 0);
        }
        return result;
    }

    static string DamageType(ChampInfo c) => c.Magic > c.Attack ? "AP" : "AD";

    // =================================================================== SELEÇÃO DE CAMPEÕES

    public async Task<ChampSelectView> ChampSelectAsync(ChampSelectInput input)
    {
        var s = settings();
        var session = input.Session;
        var view = new ChampSelectView();
        var me = session["localPlayerCellId"]?.GetValue<long>() ?? -1;

        // Ações (bans e picks) concluídas e em andamento
        var actions = (session["actions"] as JsonArray ?? []).SelectMany(g => g as JsonArray ?? []).Where(a => a != null).ToList();
        var lockedCells = actions.Where(a => a!["type"]?.GetValue<string>() == "pick" && a["completed"]?.GetValue<bool>() == true)
            .Select(a => a!["actorCellId"]!.GetValue<long>()).ToHashSet();
        var bans = actions.Where(a => a!["type"]?.GetValue<string>() == "ban" && a["completed"]?.GetValue<bool>() == true)
            .Select(a => a!["championId"]!.GetValue<int>()).Where(id => id > 0).ToHashSet();
        foreach (var b in (session["bans"]?["myTeamBans"] as JsonArray ?? []).Concat(session["bans"]?["theirTeamBans"] as JsonArray ?? []))
            if (b?.GetValue<int>() is int bid and > 0) bans.Add(bid);
        view.Bans = [.. bans];

        var myAction = actions.FirstOrDefault(a => a!["actorCellId"]?.GetValue<long>() == me && a["isInProgress"]?.GetValue<bool>() == true);
        view.MyTurnToBan = myAction?["type"]?.GetValue<string>() == "ban";
        view.MyTurnToPick = myAction?["type"]?.GetValue<string>() == "pick";
        view.Stage = session["timer"]?["phase"]?.GetValue<string>() switch
        {
            "PLANNING" => "Declaração de intenção",
            "BAN_PICK" => view.MyTurnToBan ? "Sua vez de banir" : view.MyTurnToPick ? "Sua vez de escolher" : "Bans e escolhas",
            "FINALIZATION" => "Finalização",
            var x => x ?? "",
        };
        view.Final = session["timer"]?["phase"]?.GetValue<string>() == "FINALIZATION";
        var cellsWithPick = actions.Where(a => a!["type"]?.GetValue<string>() == "pick").Select(a => a!["actorCellId"]!.GetValue<long>()).ToHashSet();
        view.SecondsLeft = (int)Math.Max(0, (session["timer"]?["adjustedTimeLeftInPhase"]?.GetValue<double>() ?? 0) / 1000);

        foreach (var p in session["myTeam"] as JsonArray ?? [])
        {
            var cell = p!["cellId"]!.GetValue<long>();
            var champ = p["championId"]?.GetValue<int>() ?? 0;
            // Modos sem etapa de escolha (ex.: ARAM): o campeão já vem definido, conta como confirmado.
            var given = champ > 0 && !cellsWithPick.Contains(cell);
            if (champ == 0) champ = p["championPickIntent"]?.GetValue<int>() ?? 0;
            var puuid = p["puuid"]?.GetValue<string>() ?? "";
            var name = p["gameName"]?.GetValue<string>();
            view.Allies.Add(new Slot
            {
                CellId = (int)cell, ChampionId = champ, Locked = lockedCells.Contains(cell) || given, IsMe = cell == me,
                Position = Pos.From(p["assignedPosition"]?.GetValue<string>()),
                Name = string.IsNullOrEmpty(name) ? null : name,
                Puuid = string.IsNullOrEmpty(puuid) ? null : puuid,
                Rank = input.RanksByPuuid.GetValueOrDefault(puuid),
            });
        }
        foreach (var p in session["theirTeam"] as JsonArray ?? [])
        {
            var cell = p!["cellId"]!.GetValue<long>();
            view.Enemies.Add(new Slot
            {
                CellId = (int)cell, ChampionId = p["championId"]?.GetValue<int>() ?? 0, Locked = lockedCells.Contains(cell),
                Position = Pos.From(p["assignedPosition"]?.GetValue<string>()),
            });
        }

        var mySlot = view.Allies.FirstOrDefault(a => a.IsMe);
        view.MyChampionId = mySlot?.ChampionId ?? 0;
        view.MyLocked = mySlot?.Locked ?? false;
        view.Mode = input.Mode;

        if (input.Mode != AnalisadorLoL.Mode.Classic) return await ModeChampSelectAsync(view, input);

        var tier = await meta.GetTierListAsync(s.Region, s.Tier);
        if (tier == null) { view.Notice = "Não foi possível baixar o meta (op.gg). Verifique a internet."; return view; }
        var rates = RoleRates(tier);

        // Rotas sem atribuição (partidas normais às cegas, inimigos ocultos): inferir pelo meta.
        foreach (var team in new[] { view.Allies, view.Enemies })
        {
            var taken = team.Where(x => x.Position != null).Select(x => x.Position!).ToList();
            var guess = AssignRoles(team.Where(x => x.Position == null).Select(x => x.ChampionId).ToList(), rates, taken);
            foreach (var x in team.Where(x => x.Position == null && guess.ContainsKey(x.ChampionId)))
                (x.Position, x.PositionConfidence) = guess[x.ChampionId];
        }

        // Minha rota: manual > atribuída pelo cliente > deduzida pelo campeão
        if (input.ManualPosition != null) { view.MyPosition = input.ManualPosition; view.PositionSource = "manual"; }
        else if (mySlot?.Position != null && mySlot.PositionConfidence == null) { view.MyPosition = mySlot.Position; view.PositionSource = "atribuída"; }
        else if (mySlot?.Position != null) { view.MyPosition = mySlot.Position; view.PositionSource = "deduzida"; }
        var pos = view.MyPosition;

        view.LaneOpponent = pos == null ? null : view.Enemies.FirstOrDefault(e => e.ChampionId > 0 && e.Position == pos);
        var oppId = view.LaneOpponent?.ChampionId ?? 0;

        var oppBuildTask = oppId > 0 && pos != null ? meta.GetBuildAsync(oppId, pos, s.Region, s.Tier) : Task.FromResult<ChampionBuild?>(null);
        var myBuildTask = view.MyChampionId > 0 && pos != null ? meta.GetBuildAsync(view.MyChampionId, pos, s.Region, s.Tier) : Task.FromResult<ChampionBuild?>(null);
        var oppBuild = await oppBuildTask;
        var myBuild = await myBuildTask;
        view.MyBuild = myBuild;

        var unavailable = new HashSet<int>(bans);
        foreach (var x in view.Allies.Where(a => !a.IsMe).Concat(view.Enemies)) if (x.ChampionId > 0) unavailable.Add(x.ChampionId);

        // Counters do oponente de rota (do ponto de vista de quem o enfrenta)
        if (oppBuild != null)
            view.LaneCounters = oppBuild.Counters.Where(m => m.Play >= 80 && !unavailable.Contains(m.ChampionId))
                .Select(m => new Matchup(m.ChampionId, m.Play, Smooth(1 - m.WinRate, m.Play, 0.5, 100)))
                .OrderByDescending(m => m.WinRate).Take(10).ToList();

        if (oppId > 0 && view.MyChampionId > 0)
        {
            var mm = myBuild?.Counters.FirstOrDefault(m => m.ChampionId == oppId);
            var om = oppBuild?.Counters.FirstOrDefault(m => m.ChampionId == view.MyChampionId);
            view.MyMatchup = mm != null ? mm : om != null ? new Matchup(oppId, om.Play, 1 - om.WinRate) : null;
        }

        static List<PlayerView> Team(IEnumerable<Slot> slots) =>
            slots.Where(x => x.ChampionId > 0).Select(x => new PlayerView { ChampionId = x.ChampionId, Position = x.Position }).ToList();
        if (view.Allies.Any(a => a.ChampionId > 0)) view.AllyDamage = Situational.Damage(meta, Team(view.Allies));
        if (view.Enemies.Any(e => e.ChampionId > 0)) view.EnemyDamage = Situational.Damage(meta, Team(view.Enemies));

        if (pos != null)
        {
            view.Suggestions = Suggest(tier, pos, view, unavailable, oppBuild, input);
            view.BanSuggestions = SuggestBans(tier, pos, view, myBuild, input);
        }
        else view.Notice = "Escolha sua rota para ver as sugestões.";
        return view;
    }

    /// <summary>
    /// Seleção nos modos sem rota. ARAM: compara seu campeão com os do banco de trocas.
    /// Arena: força no modo + sinergia com a sua dupla. URF/Nexus Blitz: força no modo + composição.
    /// </summary>
    async Task<ChampSelectView> ModeChampSelectAsync(ChampSelectView view, ChampSelectInput input)
    {
        var s = settings();
        var mode = input.Mode;
        var session = input.Session;
        view.PositionSource = AnalisadorLoL.Mode.Name(mode);
        // Arena 3x6: o cliente manda todos os lugares da seleção, mas só os do seu time vêm preenchidos
        if (mode == AnalisadorLoL.Mode.Arena) view.Allies = view.Allies.Where(a => a.IsMe || a.Puuid != null || a.ChampionId > 0).ToList();
        if (mode == AnalisadorLoL.Mode.Unsupported) { view.Notice = "Este modo não tem estatísticas públicas: o app mostra só os times."; return view; }

        static List<PlayerView> Team(IEnumerable<Slot> slots) =>
            slots.Where(x => x.ChampionId > 0).Select(x => new PlayerView { ChampionId = x.ChampionId, Position = x.Position }).ToList();
        if (view.Allies.Any(a => a.ChampionId > 0)) view.AllyDamage = Situational.Damage(meta, Team(view.Allies));
        if (view.Enemies.Any(e => e.ChampionId > 0)) view.EnemyDamage = Situational.Damage(meta, Team(view.Enemies));

        var list = await meta.GetModeListAsync(mode, s.Region);
        if (list == null) { view.Notice = "Não foi possível baixar as estatísticas do modo (op.gg). Verifique a internet."; return view; }
        var byId = list.GroupBy(e => e.ChampionId).ToDictionary(g => g.Key, g => g.First());

        // Banco do ARAM (o cliente usa um destes dois formatos)
        foreach (var b in session["benchChampions"] as JsonArray ?? [])
            if (b?["championId"]?.GetValue<int>() is int bid and > 0) view.Bench.Add(bid);
        foreach (var b in session["benchChampionIds"] as JsonArray ?? [])
            if (b?.GetValue<int>() is int bid2 and > 0 && !view.Bench.Contains(bid2)) view.Bench.Add(bid2);
        view.RerollsRemaining = session["rerollsRemaining"]?.GetValue<int>() ?? 0;

        var taken = new HashSet<int>(view.Bans);
        foreach (var x in view.Allies.Where(a => !a.IsMe).Concat(view.Enemies)) if (x.ChampionId > 0) taken.Add(x.ChampionId);

        IEnumerable<(int id, string? source)> candidates = AnalisadorLoL.Mode.IsRandom(mode)
            ? new[] { (view.MyChampionId, (string?)"seu") }.Where(c => c.Item1 > 0).Concat(view.Bench.Select(b => (b, (string?)"banco")))
            : byId.Keys.Where(id => !taken.Contains(id) && (!s.OwnedOnly || input.Owned == null || input.Owned.Contains(id))).Select(id => (id, (string?)null));

        // Arena: sinergia com a dupla já escolhida
        var partners = mode == AnalisadorLoL.Mode.Arena ? view.Allies.Where(a => !a.IsMe && a.ChampionId > 0).ToList() : [];
        var partnerSyn = new List<(Slot who, Dictionary<int, Synergy> syn)>();
        foreach (var pa in partners)
            if (await meta.GetModeBuildAsync(pa.ChampionId, mode, s.Region) is { } pb)
                partnerSyn.Add((pa, pb.Synergies.GroupBy(x => x.ChampionId).ToDictionary(g => g.Key, g => g.First())));

        var allyChamps = view.Allies.Where(a => !a.IsMe && a.ChampionId > 0 && meta.Champions.ContainsKey(a.ChampionId)).Select(a => meta.Champions[a.ChampionId]).ToList();
        int ad = allyChamps.Count(c => DamageType(c) == "AD" && c.Tags[0] != "Tank");
        int ap = allyChamps.Count(c => DamageType(c) == "AP" && c.Tags[0] != "Tank");
        bool hasFront = allyChamps.Any(c => c.Tags[0] == "Tank" || (c.Tags.Contains("Tank") && c.Defense >= 6));
        var modeName = AnalisadorLoL.Mode.Name(mode);

        var sugs = new List<Suggestion>();
        foreach (var (id, source) in candidates.DistinctBy(c => c.id))
        {
            if (!meta.Champions.TryGetValue(id, out var champ)) continue;
            byId.TryGetValue(id, out var e);
            var sug = new Suggestion
            {
                ChampionId = id, WinRate = e?.WinRate ?? 0, PickRate = e?.PickRate ?? 0, Tier = e?.Tier ?? 0, Source = source, AvgPlace = e?.AvgPlace,
                Owned = input.Owned == null || input.Owned.Contains(id),
            };
            double score = -5;
            if (e != null)
            {
                var wr = Smooth(e.WinRate, e.Play, 0.5, 1000);
                score = (wr - 0.5) * 100 + 0.5 * e.Tier switch { 1 => 1.5, 2 => 1, 3 => 0, 4 => -0.5, 5 => -1, _ => 0 };
                if (mode == AnalisadorLoL.Mode.Arena && e.AvgPlace is double ap0)
                    sug.Reasons.Add($"Posição média {ap0.ToString("0.0", Br)} · 1º lugar {Pct(e.FirstRate ?? 0)}");
                else if (wr >= 0.512) sug.Reasons.Add($"Forte no {modeName}: {Pct(e.WinRate)} de vitórias");
                else if (wr <= 0.485) sug.Reasons.Add($"Fraco no {modeName}: {Pct(e.WinRate)}");
            }
            else sug.Reasons.Add("Sem estatísticas neste modo");

            foreach (var (who, synMap) in partnerSyn)
            {
                if (!synMap.TryGetValue(id, out var syn) || syn.Play == 0) continue;
                var sw = Smooth(syn.WinRate, syn.Play, e?.WinRate ?? 0.5, 200);
                score += (sw - (e?.WinRate ?? 0.5)) * 150 / partnerSyn.Count;
                sug.MatchupWinRate = syn.WinRate;
                sug.MatchupGames = syn.Play;
                if (sw >= (e?.WinRate ?? 0.5) + 0.01) sug.Reasons.Add($"Combina com {Name(who.ChampionId)}: {Pct(syn.WinRate)} em {Num(syn.Play)} jogos");
            }

            if (mode != AnalisadorLoL.Mode.Arena && allyChamps.Count >= 2)
            {
                var type = DamageType(champ);
                if (type == "AP" && ad - ap >= 2) { score += 2; sug.Reasons.Add("Seu time precisa de dano mágico"); }
                else if (type == "AD" && ap - ad >= 2) { score += 2; sug.Reasons.Add("Seu time precisa de dano físico"); }
                if (!hasFront && allyChamps.Count >= 3 && champ.Tags[0] == "Tank") { score += 1.5; sug.Reasons.Add("Time sem linha de frente"); }
            }
            if (input.Mastery.TryGetValue(id, out var pts) && pts > 0)
            {
                sug.Mastery = pts;
                score += s.MasteryWeight * Math.Min(4, 1.6 * Math.Log10(pts / 1000.0 + 1));
                if (pts >= 20000) sug.Reasons.Add($"Você domina: {Num(pts)} pts de maestria");
            }
            sug.Score = Math.Round(score, 1);
            sugs.Add(sug);
        }
        view.Suggestions = sugs.OrderByDescending(x => x.Score).Take(AnalisadorLoL.Mode.IsRandom(mode) ? 20 : 12).ToList();

        if (AnalisadorLoL.Mode.IsRandom(mode))
        {
            var mine = view.Suggestions.FirstOrDefault(x => x.Source == "seu");
            var best = view.Suggestions.FirstOrDefault(x => x.Source == "banco");
            if (mine != null && best != null && best.Score - mine.Score >= 1.5)
                view.Notice = $"Vale trocar: {Name(best.ChampionId)} está no banco ({Pct(best.WinRate)} de vitórias no {modeName}, contra {Pct(mine.WinRate)} do seu {Name(mine.ChampionId)}).";
        }

        if (view.MyChampionId > 0) view.MyBuild = await meta.GetModeBuildAsync(view.MyChampionId, mode, s.Region);
        return view;
    }

    List<Suggestion> Suggest(List<TierEntry> tier, string pos, ChampSelectView view, HashSet<int> unavailable, ChampionBuild? oppBuild, ChampSelectInput input)
    {
        var s = settings();
        var oppId = view.LaneOpponent?.ChampionId ?? 0;
        var oppName = oppId > 0 ? Name(oppId) : "";

        // Composição aliada (sem contar comigo)
        var allyChamps = view.Allies.Where(a => !a.IsMe && a.ChampionId > 0 && meta.Champions.ContainsKey(a.ChampionId))
            .Select(a => meta.Champions[a.ChampionId]).ToList();
        int ad = allyChamps.Count(c => DamageType(c) == "AD" && c.Tags[0] != "Tank");
        int ap = allyChamps.Count(c => DamageType(c) == "AP" && c.Tags[0] != "Tank");
        bool hasFront = allyChamps.Any(c => c.Tags[0] == "Tank" || (c.Tags.Contains("Tank") && c.Defense >= 6));

        var list = new List<Suggestion>();
        foreach (var e in tier.Where(e => e.Position == pos && e.PickRate >= 0.003 && e.RoleRate >= 0.05))
        {
            if (unavailable.Contains(e.ChampionId) || !meta.Champions.TryGetValue(e.ChampionId, out var champ)) continue;
            var owned = input.Owned == null || input.Owned.Contains(e.ChampionId);
            if (s.OwnedOnly && !owned) continue;

            var sug = new Suggestion { ChampionId = e.ChampionId, WinRate = e.WinRate, PickRate = e.PickRate, Tier = e.Tier, Owned = owned };
            // Base da nota: taxa de vitória esperada. Com oponente conhecido, usa o confronto direto
            // (suavizado pela média do campeão: poucos jogos pesam pouco).
            var wr = Smooth(e.WinRate, e.Play, 0.5, 2000);
            var expected = wr;
            if (wr >= 0.515) sug.Reasons.Add($"Forte no meta: {Pct(e.WinRate)} de vitórias");

            var m = oppBuild?.Counters.FirstOrDefault(c => c.ChampionId == e.ChampionId);
            if (m != null && m.Play > 0)
            {
                expected = Smooth(1 - m.WinRate, m.Play, wr, 300);
                sug.MatchupWinRate = 1 - m.WinRate;
                sug.MatchupGames = m.Play;
                if (expected >= 0.512) sug.Reasons.Add($"Vence {oppName}: {Pct(1 - m.WinRate)} em {Num(m.Play)} jogos");
                else if (expected <= 0.488) sug.Reasons.Add($"Desvantagem contra {oppName} ({Pct(1 - m.WinRate)})");
            }
            double score = (expected - 0.5) * 100 + 0.5 * e.Tier switch { 1 => 1.5, 2 => 1, 3 => 0, 4 => -0.5, 5 => -1, _ => 0 };

            // Equilíbrio de dano e linha de frente
            if (allyChamps.Count >= 2)
            {
                var type = DamageType(champ);
                if (type == "AP" && ad - ap >= 2) { score += 2; sug.Reasons.Add("Seu time precisa de dano mágico"); }
                else if (type == "AD" && ap - ad >= 2) { score += 2; sug.Reasons.Add("Seu time precisa de dano físico"); }
                if (!hasFront && allyChamps.Count >= 3 && champ.Tags[0] == "Tank") { score += 1.5; sug.Reasons.Add("Time sem linha de frente"); }
            }

            // Conforto: maestria do jogador
            if (input.Mastery.TryGetValue(e.ChampionId, out var pts) && pts > 0)
            {
                sug.Mastery = pts;
                score += s.MasteryWeight * Math.Min(4, 1.6 * Math.Log10(pts / 1000.0 + 1));
                if (pts >= 20000) sug.Reasons.Add($"Você domina: {Num(pts)} pts de maestria");
            }
            sug.Score = Math.Round(score, 1);
            list.Add(sug);
        }
        return list.OrderByDescending(x => x.Score).Take(12).ToList();
    }

    List<Suggestion> SuggestBans(List<TierEntry> tier, string pos, ChampSelectView view, ChampionBuild? myBuild, ChampSelectInput input)
    {
        var taken = new HashSet<int>(view.Bans);
        foreach (var x in view.Allies.Concat(view.Enemies)) if (x.ChampionId > 0) taken.Add(x.ChampionId);
        var myName = view.MyChampionId > 0 ? Name(view.MyChampionId) : "";
        var threats = myBuild?.Counters.Where(m => m.Play >= 100).ToDictionary(m => m.ChampionId) ?? [];

        var list = new List<Suggestion>();
        foreach (var e in tier.Where(e => e.Position == pos && e.PickRate >= 0.005))
        {
            if (taken.Contains(e.ChampionId) || !meta.Champions.ContainsKey(e.ChampionId)) continue;
            var sug = new Suggestion { ChampionId = e.ChampionId, WinRate = e.WinRate, PickRate = e.PickRate, Tier = e.Tier };
            var wr = Smooth(e.WinRate, e.Play, 0.5, 2000);
            double score = (wr - 0.5) * 100 + e.BanRate * 15 + e.PickRate * 20;
            if (wr >= 0.512) sug.Reasons.Add($"Forte no meta: {Pct(e.WinRate)}");
            if (e.BanRate >= 0.15) sug.Reasons.Add($"Muito banido: {Pct(e.BanRate)}");
            if (threats.TryGetValue(e.ChampionId, out var t) && t.WinRate < 0.48)
            {
                score += (0.5 - Smooth(t.WinRate, t.Play, 0.5, 150)) * 150;
                sug.MatchupWinRate = t.WinRate;
                sug.Reasons.Add($"Counter do seu {myName} (você ganha {Pct(t.WinRate)})");
            }
            sug.Score = Math.Round(score, 1);
            list.Add(sug);
        }
        return list.OrderByDescending(x => x.Score).Take(6).ToList();
    }

    // =================================================================== PARTIDA EM ANDAMENTO

    static bool arenaFieldsLogged;

    int ChampionFromLive(JsonNode p)
    {
        var raw = p["rawChampionName"]?.GetValue<string>() ?? "";
        var i = raw.IndexOf("displayname_", StringComparison.OrdinalIgnoreCase);
        if (i >= 0 && meta.ChampionByKey.TryGetValue(raw[(i + 12)..], out var id)) return id;
        var name = p["championName"]?.GetValue<string>() ?? "";
        return meta.Champions.Values.FirstOrDefault(c => c.Name.Equals(name, StringComparison.OrdinalIgnoreCase) || c.Key.Equals(name, StringComparison.OrdinalIgnoreCase))?.Id ?? 0;
    }

    int SpellFromLive(JsonNode? sp)
    {
        var raw = sp?["rawDisplayName"]?.GetValue<string>() ?? "";
        var parts = raw.Split('_');
        return parts.Length >= 3 && meta.SpellByKey.TryGetValue(parts[2], out var id) ? id : 0;
    }

    public async Task<GameView> GameAsync(GameInput input)
    {
        var s = settings();
        var d = input.Data;
        var view = new GameView
        {
            GameTime = d["gameData"]?["gameTime"]?.GetValue<double>() ?? 0,
            GameMode = d["gameData"]?["gameMode"]?.GetValue<string>() ?? "",
        };
        var active = d["activePlayer"];
        var myId = active?["riotId"]?.GetValue<string>() ?? active?["summonerName"]?.GetValue<string>() ?? "";

        var players = new List<PlayerView>();
        foreach (var p in d["allPlayers"] as JsonArray ?? [])
        {
            if (p == null) continue;
            var riotId = p["riotId"]?.GetValue<string>() ?? p["summonerName"]?.GetValue<string>() ?? "";
            var items = (p["items"] as JsonArray ?? []).Select(i => i!["itemID"]!.GetValue<int>()).ToArray();
            var sc = p["scores"];
            var pv = new PlayerView
            {
                RiotId = riotId, ChampionId = ChampionFromLive(p), Team = p["team"]?.GetValue<string>() ?? "",
                Position = Pos.From(p["position"]?.GetValue<string>()), Level = p["level"]?.GetValue<int>() ?? 1,
                Kills = sc?["kills"]?.GetValue<int>() ?? 0, Deaths = sc?["deaths"]?.GetValue<int>() ?? 0,
                Assists = sc?["assists"]?.GetValue<int>() ?? 0, Cs = sc?["creepScore"]?.GetValue<int>() ?? 0,
                Items = items, IsDead = p["isDead"]?.GetValue<bool>() ?? false, Respawn = p["respawnTimer"]?.GetValue<double>() ?? 0,
                Spells = [SpellFromLive(p["summonerSpells"]?["summonerSpellOne"]), SpellFromLive(p["summonerSpells"]?["summonerSpellTwo"])],
                IsMe = riotId == myId, Rank = input.RanksByRiotId.GetValueOrDefault(riotId),
            };
            pv.ItemGold = (p["items"] as JsonArray ?? []).Sum(i => meta.Items.TryGetValue(i!["itemID"]!.GetValue<int>(), out var it)
                ? it.Gold * Math.Max(1, i["count"]?.GetValue<int>() ?? 1) : 0);
            pv.Index = players.Count;
            players.Add(pv);
        }
        var meP = players.FirstOrDefault(p => p.IsMe) ?? players.FirstOrDefault();
        if (meP == null) return view;
        meP.IsMe = true;
        view.Me = meP;
        view.Mode = input.Mode;
        var mode = input.Mode;
        if (mode == AnalisadorLoL.Mode.Arena)
        {
            // Arena: a API da partida não diz o time de ninguém (todos vêm como ORDER) e a ordem da lista não é por time.
            // O time vem da seleção de campeões: reconhece pelo nome; com nome oculto, pelo campeão (se só houver um).
            var champCount = players.GroupBy(p => p.ChampionId).ToDictionary(g => g.Key, g => g.Count());
            // Caminho exato: a lista do cliente vem na mesma ordem da partida (confere campeão a campeão antes de usar)
            var teamPuuids = input.ArenaTeam.Select(m => m.Puuid).ToHashSet();
            var aligned = input.ArenaOrder.Count == players.Count && players.Select((p, i) => p.ChampionId == input.ArenaOrder[i].ChampionId).All(x => x);
            bool IsMate(PlayerView p) => aligned ? teamPuuids.Contains(input.ArenaOrder[players.IndexOf(p)].Puuid) : input.ArenaTeam.Any(m =>
                (!string.IsNullOrEmpty(m.Name) && p.RiotId.Split('#')[0].Equals(m.Name, StringComparison.OrdinalIgnoreCase))
                || (m.ChampionId == p.ChampionId && champCount.GetValueOrDefault(p.ChampionId) == 1));
            view.Allies = players.Where(p => p.IsMe || (input.ArenaTeam.Count > 0 && IsMate(p))).ToList();
            // Time guardado de outra partida não conta: só vale se encontrou alguém
            view.ArenaTeamKnown = view.Allies.Count > 1;
            view.ArenaTeamSize = input.ArenaTeamSize > 0 ? input.ArenaTeamSize : players.Count is 18 or 12 or 9 ? 3 : 2;
            foreach (var p in view.Allies) p.Subteam = 1;
            if (!arenaFieldsLogged) { arenaFieldsLogged = true; Log.Write($"Arena: {players.Count} jogadores; seu time reconhecido: {string.Join(", ", view.Allies.Select(p => Name(p.ChampionId)))}."); }
            view.Enemies = players.Where(p => !view.Allies.Contains(p)).ToList();
        }
        else
        {
            view.Allies = players.Where(p => p.Team == meP.Team).ToList();
            view.Enemies = players.Where(p => p.Team != meP.Team).ToList();
        }

        // Rotas ausentes (modos sem rota definida): memória da seleção ou dedução pelo meta
        var tier = AnalisadorLoL.Mode.HasLanes(mode) ? await meta.GetTierListAsync(s.Region, s.Tier) : null;
        var rates = tier != null ? RoleRates(tier) : [];
        foreach (var team in AnalisadorLoL.Mode.HasLanes(mode) ? new[] { view.Allies, view.Enemies } : [])
        {
            foreach (var p in team.Where(p => p.Position == null))
                if (input.KnownPositions.TryGetValue(p.ChampionId, out var kp) && team.All(o => o.Position != kp)) p.Position = kp;
            var missing = team.Where(p => p.Position == null).ToList();
            if (missing.Count == 0) continue;
            var forced = missing.Where(p => p.Spells.Contains(11)).ToDictionary(p => p.ChampionId, _ => Pos.Jungle); // Golpear = selva
            var guess = AssignRoles(missing.Select(p => p.ChampionId).ToList(), rates, team.Where(p => p.Position != null).Select(p => p.Position!).ToList(), forced);
            foreach (var p in missing) if (guess.TryGetValue(p.ChampionId, out var g)) p.Position = g.pos;
        }
        view.MyPosition = meP.Position;
        view.LaneOpponent = view.Enemies.FirstOrDefault(e => e.Position != null && e.Position == meP.Position);
        (view.AllyObjectives, view.EnemyObjectives) = Objectives(d, meP.Team, players);
        if (mode == AnalisadorLoL.Mode.Arena)
        {
            // Na Arena todos vêm no mesmo "time" da API: abates contados pelo seu time de verdade
            view.AllyObjectives.Kills = view.Allies.Sum(p => p.Kills);
            view.EnemyObjectives.Kills = view.Enemies.Sum(p => p.Kills);
        }
        view.AllyGold = view.Allies.Sum(p => p.ItemGold);
        view.EnemyGold = view.Enemies.Sum(p => p.ItemGold);

        if (!AnalisadorLoL.Mode.HasLanes(mode) && meP.ChampionId > 0)
        {
            // Modos sem rota: build do próprio modo (a "rota" guardada é o nome do modo)
            var key = mode.ToUpperInvariant();
            var shop = input.ShopBuild is { } sb0 && sb0.Champ == meP.ChampionId && sb0.Pos == key ? sb0 : ((int, string, string, string)?)null;
            view.BuildRegion = shop is { } y ? y.Item3 : s.Region;
            view.BuildTier = "";
            view.SameAsShop = shop != null;
            var build = mode == AnalisadorLoL.Mode.Unsupported ? null : await meta.GetModeBuildAsync(meP.ChampionId, mode, view.BuildRegion);
            view.Build = build;
            if (build != null) FillBuildPath(view, build, meP, active);
        }
        else if (meP.Position != null && meP.ChampionId > 0)
        {
            // Mostra a mesma build que foi para a loja do jogo (o jogo só lê a loja ao carregar a partida).
            var shop = input.ShopBuild is { } sb && sb.Champ == meP.ChampionId && sb.Pos == meP.Position ? sb : ((int, string, string, string)?)null;
            (view.BuildRegion, view.BuildTier) = shop is { } x ? (x.Item3, x.Item4) : (s.Region, s.Tier);
            view.SameAsShop = shop != null;
            var build = await meta.GetBuildAsync(meP.ChampionId, meP.Position, view.BuildRegion, view.BuildTier);
            view.Build = build;
            if (build != null) FillBuildPath(view, build, meP, active);
            if (build != null && view.LaneOpponent != null)
                view.MyMatchup = build.Counters.FirstOrDefault(m => m.ChampionId == view.LaneOpponent.ChampionId);
        }

        var (threats, situational) = Situational.Analyze(meta, meP, view.Enemies, view.Build, mode);
        view.Threats = threats;
        view.Situational = situational;
        view.AllyDamage = Situational.Damage(meta, view.Allies);
        return view;
    }

    /// <summary>
    /// Torres, inibidores, dragões, barão, arauto e larvas por time, a partir dos eventos da API da partida.
    /// Torres e inibidores dizem o lado no nome (T1 = azul/ORDER, T2 = vermelho/CHAOS); monstros pelo jogador que abateu.
    /// </summary>
    static (TeamObjectives ally, TeamObjectives enemy) Objectives(JsonNode d, string myTeam, List<PlayerView> players)
    {
        var ally = new TeamObjectives { Kills = players.Where(p => p.Team == myTeam).Sum(p => p.Kills) };
        var enemy = new TeamObjectives { Kills = players.Where(p => p.Team != myTeam).Sum(p => p.Kills) };
        var teamByName = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var p in d["allPlayers"] as JsonArray ?? [])
        {
            var team = p?["team"]?.GetValue<string>() ?? "";
            foreach (var k in new[] { "riotId", "riotIdGameName", "summonerName" })
                if (p?[k]?.GetValue<string>() is { Length: > 0 } n) teamByName[n] = team;
        }
        TeamObjectives? For(string? team) => team == null ? null : team == myTeam ? ally : enemy;
        TeamObjectives? ByKiller(JsonNode ev) => For(ev["KillerName"]?.GetValue<string>() is { } k && teamByName.TryGetValue(k, out var t) ? t : null);
        // Estrutura destruída do lado T1 (ORDER) conta ponto para CHAOS, e vice-versa
        TeamObjectives? ByStructure(string? name) => name == null ? null
            : name.Contains("_T1_") || name.Contains("_T1") && name.StartsWith("Barracks") ? For("CHAOS")
            : name.Contains("_T2_") || name.Contains("_T2") && name.StartsWith("Barracks") ? For("ORDER") : null;

        foreach (var ev in d["events"]?["Events"] as JsonArray ?? [])
        {
            if (ev == null) continue;
            switch (ev["EventName"]?.GetValue<string>())
            {
                case "TurretKilled": if (ByStructure(ev["TurretKilled"]?.GetValue<string>()) is { } t1) t1.Towers++; break;
                case "InhibKilled": if (ByStructure(ev["InhibKilled"]?.GetValue<string>()) is { } t2) t2.Inhibitors++; break;
                case "DragonKill": if (ByKiller(ev) is { } t3) t3.Dragons.Add(ev["DragonType"]?.GetValue<string>() ?? "?"); break;
                case "BaronKill": if (ByKiller(ev) is { } t4) t4.Barons++; break;
                case "HeraldKill": if (ByKiller(ev) is { } t5) t5.Heralds++; break;
                case "HordeKill": if (ByKiller(ev) is { } t6) t6.Grubs++; break;
            }
        }
        return (ally, enemy);
    }

    void FillBuildPath(GameView view, ChampionBuild build, PlayerView me, JsonNode? active)
    {
        var owned = me.Items.ToHashSet();
        string Status(int id)
        {
            if (owned.Contains(id)) return "comprado";
            if (meta.Items.TryGetValue(id, out var it) && it.From.Any(owned.Contains)) return "em-progresso";
            return "falta";
        }
        bool Exists(int id) => meta.Items.ContainsKey(id);

        // Núcleo: sempre o mais popular, o mesmo que vai para a loja do jogo.
        var core = build.Core.FirstOrDefault();
        var coreIds = core?.Ids.Where(Exists).ToList() ?? [];
        view.CorePath = coreIds.Select(id => new BuildStep { ItemId = id, Status = Status(id) }).ToList();

        // Se as compras batem mais com outra variação popular, ela aparece à parte (só informativo).
        var alt = build.Core.Skip(1).Take(5).Select(c => (c, n: c.Ids.Count(owned.Contains)))
            .Where(x => x.n > (core?.Ids.Count(owned.Contains) ?? 0)).OrderByDescending(x => x.n).Select(x => x.c).FirstOrDefault();
        if (alt != null)
        {
            view.AltCorePath = alt.Ids.Where(Exists).Select(id => new BuildStep { ItemId = id, Status = Status(id) }).ToList();
            view.AltCoreWinRate = alt.WinRate;
        }

        bool IsBoots(int id) => meta.Items.TryGetValue(id, out var it) && it.Tags.Contains("Boots") && it.Gold > 300;
        var ownedBoots = me.Items.FirstOrDefault(IsBoots);
        var bootId = ownedBoots > 0 ? ownedBoots : build.Boots.Select(b => b.Ids.FirstOrDefault()).FirstOrDefault(Exists);
        if (bootId > 0) view.Boots = new BuildStep { ItemId = bootId, Status = Status(bootId) };

        view.LateOptions = build.Late.Select(l => l.Ids.FirstOrDefault())
            .Where(id => Exists(id) && meta.Items[id].Into.Length == 0 && !coreIds.Contains(id) && !IsBoots(id)).Distinct().Take(6)
            .Select(id => new BuildStep { ItemId = id, Status = Status(id) }).ToList();

        view.NextItem = view.CorePath.FirstOrDefault(x => x.Status != "comprado")?.ItemId
                        ?? (view.Boots is { Status: not "comprado" } ? (int?)view.Boots.ItemId : null)
                        ?? view.LateOptions.FirstOrDefault(x => x.Status != "comprado")?.ItemId;

        // Próxima habilidade: pontos já gastos (soma dos níveis de Q/W/E/R) indicam a posição na ordem
        var order = build.Skills.FirstOrDefault()?.Order;
        var ab = active?["abilities"];
        if (order != null && ab != null)
        {
            int spent = new[] { "Q", "W", "E", "R" }.Sum(k => ab[k]?["abilityLevel"]?.GetValue<int>() ?? 0);
            if (spent < order.Length) { view.NextSkill = order[spent]; view.NextSkillIndex = spent; }
        }
    }
}
