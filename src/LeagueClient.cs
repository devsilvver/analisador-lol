using System.Diagnostics;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json.Nodes;

namespace AnalisadorLoL;

/// <summary>
/// Acesso à API local do cliente do LoL (LCU). As credenciais vêm do arquivo "lockfile"
/// da pasta de instalação. Só aceita certificado de 127.0.0.1 (o cliente usa certificado próprio).
/// </summary>
public sealed class LeagueClient : IDisposable
{
    readonly HttpClient http;
    readonly Func<string?> customPath;
    string? baseUrl;
    AuthenticationHeaderValue? auth;
    DateTime nextDiscovery = DateTime.MinValue;

    public LeagueClient(Func<string?> customPath)
    {
        this.customPath = customPath;
        var handler = new SocketsHttpHandler
        {
            PooledConnectionIdleTimeout = TimeSpan.FromSeconds(30),
            SslOptions = { RemoteCertificateValidationCallback = (_, _, _, _) => true },
        };
        http = new HttpClient(handler) { Timeout = TimeSpan.FromSeconds(4) };
    }

    public bool Connected => baseUrl != null;

    public bool EnsureCredentials()
    {
        if (baseUrl != null) return true;
        if (DateTime.UtcNow < nextDiscovery) return false;
        nextDiscovery = DateTime.UtcNow.AddSeconds(5); // procura no máximo a cada 5 s

        foreach (var file in CandidateLockfiles())
        {
            try
            {
                if (!File.Exists(file)) continue;
                using var fs = new FileStream(file, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
                using var sr = new StreamReader(fs);
                var parts = sr.ReadToEnd().Split(':');
                if (parts.Length < 5) continue;
                baseUrl = $"https://127.0.0.1:{parts[2]}";
                auth = new AuthenticationHeaderValue("Basic", Convert.ToBase64String(Encoding.ASCII.GetBytes("riot:" + parts[3])));
                return true;
            }
            catch { }
        }
        return false;
    }

    IEnumerable<string> CandidateLockfiles()
    {
        var custom = customPath();
        if (!string.IsNullOrWhiteSpace(custom)) yield return Path.Combine(custom, "lockfile");

        // A pasta do processo é a forma mais confiável (funciona em qualquer disco).
        string? dir = null;
        foreach (var p in Process.GetProcessesByName("LeagueClientUx"))
        {
            try { dir = Path.GetDirectoryName(p.MainModule?.FileName); } catch { }
            p.Dispose();
            if (dir != null) break;
        }
        if (dir != null) yield return Path.Combine(dir, "lockfile");

        foreach (var drive in new[] { "C", "D", "E", "F" })
        {
            yield return $@"{drive}:\Riot Games\League of Legends\lockfile";
            yield return $@"{drive}:\Program Files\Riot Games\League of Legends\lockfile";
            yield return $@"{drive}:\Games\Riot Games\League of Legends\lockfile";
        }
    }

    void Reset() { baseUrl = null; auth = null; }

    public async Task<HttpResponseMessage?> SendAsync(HttpMethod method, string path, JsonNode? body = null)
    {
        if (!EnsureCredentials()) return null;
        try
        {
            using var req = new HttpRequestMessage(method, baseUrl + path);
            req.Headers.Authorization = auth;
            if (body != null) req.Content = new StringContent(body.ToJsonString(), Encoding.UTF8, "application/json");
            return await http.SendAsync(req);
        }
        catch (HttpRequestException) { Reset(); return null; }
        catch (TaskCanceledException) { return null; }
    }

    public async Task<JsonNode?> GetAsync(string path)
    {
        using var res = await SendAsync(HttpMethod.Get, path);
        if (res == null || !res.IsSuccessStatusCode) return null;
        try { return JsonNode.Parse(await res.Content.ReadAsStringAsync()); } catch { return null; }
    }

    public void Dispose() => http.Dispose();
}

/// <summary>API oficial da partida em andamento (porta 2999), disponível só durante o jogo.</summary>
public sealed class LiveClient : IDisposable
{
    readonly HttpClient http;

    public LiveClient()
    {
        var handler = new SocketsHttpHandler
        {
            PooledConnectionIdleTimeout = TimeSpan.FromSeconds(30),
            SslOptions = { RemoteCertificateValidationCallback = (_, _, _, _) => true },
        };
        http = new HttpClient(handler) { Timeout = TimeSpan.FromSeconds(2) };
    }

    public async Task<JsonNode?> GetAllAsync()
    {
        try
        {
            var txt = await http.GetStringAsync("https://127.0.0.1:2999/liveclientdata/allgamedata");
            var node = JsonNode.Parse(txt);
            // Na tela de carregamento a API responde, mas sem jogadores.
            return node?["allPlayers"] is JsonArray { Count: > 0 } ? node : null;
        }
        catch { return null; }
    }

    public void Dispose() => http.Dispose();
}
