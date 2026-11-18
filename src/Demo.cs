using System.Text.Json.Nodes;

namespace AnalisadorLoL;

/// <summary>Dados fictícios para ver a interface sem estar numa seleção ou partida.</summary>
public static class Demo
{
    /// <summary>Na demonstração da seleção: você está em duo com o Aliado1 (células 0 e 1).</summary>
    public static Dictionary<int, PartyTag> Parties(List<Slot> _) => new() { [0] = new(1, 2, true, 0), [1] = new(1, 2, true, 0) };

    /// <summary>Na demonstração da partida: duo aliado (Vi + Orianna) e trio inimigo provável (Lee Sin, Jinx, Soraka).</summary>
    public static readonly Dictionary<int, PartyTag> GameParties = new()
    {
        [254] = new(1, 2, true, 0), [61] = new(1, 2, true, 0),
        [64] = new(1, 3, false, 7), [222] = new(1, 3, false, 7), [16] = new(1, 3, false, 7),
    };

    public static readonly Dictionary<int, int> Mastery = new() { [86] = 120_000, [122] = 32_000, [54] = 45_000, [516] = 21_000 };

    /// <summary>"aram-selecao" → ARAM, "arena-partida" → Arena, "selecao" → Summoner's Rift.</summary>
    public static string ModeOf(string demo) => demo.StartsWith("aram") ? Mode.Aram : demo.StartsWith("arena") ? Mode.Arena : Mode.Classic;

    public static JsonNode Session(string mode) => mode switch
    {
        // ARAM: campeão sorteado (Garen) e banco com 4 opções para trocar
        Mode.Aram => JsonNode.Parse("""
        {
          "localPlayerCellId": 0, "benchEnabled": true, "rerollsRemaining": 1,
          "benchChampions": [ { "championId": 157 }, { "championId": 222 }, { "championId": 103 }, { "championId": 54 } ],
          "myTeam": [
            { "cellId": 0, "championId": 86, "gameName": "Você" }, { "cellId": 1, "championId": 254, "gameName": "Aliado1" },
            { "cellId": 2, "championId": 61, "gameName": "Aliado2" }, { "cellId": 3, "championId": 145, "gameName": "Aliado3" },
            { "cellId": 4, "championId": 111, "gameName": "Aliado4" }
          ],
          "theirTeam": [], "actions": [], "bans": { "myTeamBans": [], "theirTeamBans": [] },
          "timer": { "phase": "FINALIZATION", "adjustedTimeLeftInPhase": 42000 }
        }
        """)!,
        // Arena: sua dupla já escolheu Lee Sin; sua vez de escolher
        Mode.Arena => JsonNode.Parse("""
        {
          "localPlayerCellId": 0,
          "myTeam": [ { "cellId": 0, "championId": 0, "gameName": "Você" }, { "cellId": 1, "championId": 64, "gameName": "Dupla" } ],
          "theirTeam": [],
          "actions": [ [ { "actorCellId": 1, "championId": 64, "completed": true, "isInProgress": false, "type": "pick" } ],
                       [ { "actorCellId": 0, "championId": 0, "completed": false, "isInProgress": true, "type": "pick" } ] ],
          "bans": { "myTeamBans": [], "theirTeamBans": [] },
          "timer": { "phase": "BAN_PICK", "adjustedTimeLeftInPhase": 21000 }
        }
        """)!,
        _ => Session(),
    };

    public static JsonNode Session() => JsonNode.Parse("""
    {
      "localPlayerCellId": 0,
      "myTeam": [
        { "cellId": 0, "championId": 0, "championPickIntent": 86, "assignedPosition": "top", "gameName": "Você" },
        { "cellId": 1, "championId": 254, "assignedPosition": "jungle", "gameName": "Aliado1" },
        { "cellId": 2, "championId": 61, "assignedPosition": "middle", "gameName": "Aliado2" },
        { "cellId": 3, "championId": 0, "championPickIntent": 145, "assignedPosition": "bottom", "gameName": "Aliado3" },
        { "cellId": 4, "championId": 0, "assignedPosition": "utility", "gameName": "Aliado4" }
      ],
      "theirTeam": [
        { "cellId": 5, "championId": 122 }, { "cellId": 6, "championId": 64 }, { "cellId": 7, "championId": 103 },
        { "cellId": 8, "championId": 222 }, { "cellId": 9, "championId": 0 }
      ],
      "actions": [
        [ { "actorCellId": 0, "championId": 238, "completed": true, "isInProgress": false, "type": "ban" },
          { "actorCellId": 5, "championId": 157, "completed": true, "isInProgress": false, "type": "ban" } ],
        [ { "actorCellId": 1, "championId": 254, "completed": true, "isInProgress": false, "type": "pick" },
          { "actorCellId": 2, "championId": 61, "completed": true, "isInProgress": false, "type": "pick" },
          { "actorCellId": 5, "championId": 122, "completed": true, "isInProgress": false, "type": "pick" },
          { "actorCellId": 6, "championId": 64, "completed": true, "isInProgress": false, "type": "pick" },
          { "actorCellId": 7, "championId": 103, "completed": true, "isInProgress": false, "type": "pick" },
          { "actorCellId": 8, "championId": 222, "completed": true, "isInProgress": false, "type": "pick" } ],
        [ { "actorCellId": 0, "championId": 86, "completed": false, "isInProgress": true, "type": "pick" } ]
      ],
      "bans": { "myTeamBans": [], "theirTeamBans": [] },
      "timer": { "phase": "BAN_PICK", "adjustedTimeLeftInPhase": 24000 }
    }
    """)!;

    static string Player(string riot, string key, string team, string pos, int lvl, int k, int d, int a, int cs, int[] items, string s1, string s2) => $$"""
      { "riotId": "{{riot}}", "rawChampionName": "game_character_displayname_{{key}}", "championName": "{{key}}", "team": "{{team}}",
        "position": "{{pos}}", "level": {{lvl}}, "isDead": false, "respawnTimer": 0,
        "scores": { "kills": {{k}}, "deaths": {{d}}, "assists": {{a}}, "creepScore": {{cs}} },
        "items": [{{string.Join(",", items.Select(i => $"{{\"itemID\":{i},\"count\":1}}"))}}],
        "summonerSpells": { "summonerSpellOne": { "rawDisplayName": "GeneratedTip_SummonerSpell_{{s1}}_DisplayName" },
                            "summonerSpellTwo": { "rawDisplayName": "GeneratedTip_SummonerSpell_{{s2}}_DisplayName" } } }
    """;

    public static JsonNode Game(string mode)
    {
        if (mode == Mode.Classic) return Game();
        var g = Game();
        g["gameData"]!["gameMode"] = mode == Mode.Arena ? "CHERRY" : "ARAM";
        g["events"] = new JsonObject { ["Events"] = new JsonArray() };
        foreach (var p in g["allPlayers"]!.AsArray()) p!["position"] = "";
        if (mode == Mode.Arena)
        {
            // Arena: 8 jogadores em duplas (subtime 1..4); itens com os IDs próprios da Arena
            var arr = g["allPlayers"]!.AsArray();
            while (arr.Count > 8) arr.RemoveAt(arr.Count - 1);
            for (int i = 0; i < arr.Count; i++)
            {
                arr[i]!["subteamId"] = i / 2 + 1;
                arr[i]!["items"] = new JsonArray(new JsonObject { ["itemID"] = 223031, ["count"] = 1 }, new JsonObject { ["itemID"] = 223006, ["count"] = 1 });
            }
        }
        return g;
    }

    public static JsonNode Game()
    {
        var players = new[]
        {
            Player("Você#BR1", "Garen", "ORDER", "TOP", 11, 4, 2, 3, 142, [6631, 3006, 1037, 2055], "SummonerFlash", "SummonerTeleport"),
            Player("Aliado1#BR1", "Vi", "ORDER", "JUNGLE", 10, 3, 3, 6, 98, [6692, 3047, 1036], "SummonerFlash", "SummonerSmite"),
            Player("Aliado2#BR1", "Orianna", "ORDER", "MIDDLE", 11, 2, 1, 5, 160, [6653, 3020, 1052], "SummonerFlash", "SummonerTeleport"),
            Player("Aliado3#BR1", "Kaisa", "ORDER", "BOTTOM", 10, 5, 2, 2, 151, [3124, 3006, 1043], "SummonerFlash", "SummonerHeal"),
            Player("Aliado4#BR1", "Nautilus", "ORDER", "UTILITY", 8, 0, 3, 9, 22, [3190, 3047], "SummonerFlash", "SummonerDot"),
            Player("Inimigo1#BR1", "Aatrox", "CHAOS", "TOP", 11, 3, 3, 2, 150, [6692, 3047, 1028], "SummonerFlash", "SummonerTeleport"),
            Player("Inimigo2#BR1", "LeeSin", "CHAOS", "JUNGLE", 10, 4, 4, 3, 102, [6692, 3111], "SummonerFlash", "SummonerSmite"),
            Player("Inimigo3#BR1", "Ahri", "CHAOS", "MIDDLE", 12, 5, 2, 4, 171, [4645, 3020, 1058], "SummonerFlash", "SummonerDot"),
            Player("Inimigo4#BR1", "Jinx", "CHAOS", "BOTTOM", 10, 2, 4, 3, 140, [3031, 3006], "SummonerFlash", "SummonerHeal"),
            Player("Inimigo5#BR1", "Soraka", "CHAOS", "UTILITY", 8, 0, 2, 10, 18, [3107, 3158], "SummonerFlash", "SummonerExhaust"),
        };
        return JsonNode.Parse($$"""
        {
          "activePlayer": { "riotId": "Você#BR1", "level": 11, "currentGold": 1320,
            "abilities": { "Q": { "abilityLevel": 3 }, "W": { "abilityLevel": 1 }, "E": { "abilityLevel": 5 }, "R": { "abilityLevel": 1 } } },
          "allPlayers": [{{string.Join(",", players)}}],
          "gameData": { "gameMode": "CLASSIC", "gameTime": 1104.5 },
          "events": { "Events": [
            { "EventName": "HordeKill", "KillerName": "Aliado1#BR1" }, { "EventName": "HordeKill", "KillerName": "Aliado1#BR1" },
            { "EventName": "HordeKill", "KillerName": "Inimigo2#BR1" },
            { "EventName": "DragonKill", "DragonType": "Fire", "KillerName": "Aliado1#BR1" },
            { "EventName": "DragonKill", "DragonType": "Water", "KillerName": "Inimigo2#BR1" },
            { "EventName": "DragonKill", "DragonType": "Hextech", "KillerName": "Aliado1#BR1" },
            { "EventName": "HeraldKill", "KillerName": "Inimigo2#BR1" },
            { "EventName": "TurretKilled", "TurretKilled": "Turret_T2_R_03_A", "KillerName": "Aliado3#BR1" },
            { "EventName": "TurretKilled", "TurretKilled": "Turret_T2_C_05_A", "KillerName": "Aliado2#BR1" },
            { "EventName": "TurretKilled", "TurretKilled": "Turret_T1_L_03_A", "KillerName": "Inimigo1#BR1" }
          ] }
        }
        """)!;
    }
}
