using System.Diagnostics;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Text.Json;
using System.Text.Json.Nodes;
using Microsoft.Web.WebView2.Core;
using Microsoft.Web.WebView2.WinForms;
using Microsoft.Win32;

namespace AnalisadorLoL;

/// <summary>
/// Janela do app. Fica sempre viva na bandeja do Windows: o X esconde, sair só pelo menu da bandeja.
/// Escondida, a tela é suspensa e a partida deixa de ser lida (custo quase zero).
/// </summary>
public sealed class MainForm : Form
{
    const string Host = "https://app.local/";
    readonly WebView2 web = new() { Dock = DockStyle.Fill, DefaultBackgroundColor = Color.FromArgb(9, 12, 18) };
    readonly MetaService meta = new();
    readonly Engine engine;
    readonly NotifyIcon tray;
    readonly Task staticLoad;
    readonly KeyboardWatcher keyboard;
    CoreWebView2Environment? env;
    bool suspended, exiting, overlayShown, trayHintShown;
    IntPtr gameWindow;

    public MainForm()
    {
        Text = "Analisador LoL";
        StartPosition = FormStartPosition.CenterScreen;
        Size = new Size(1400, 880);
        MinimumSize = new Size(1040, 660);
        BackColor = Color.FromArgb(9, 12, 18);
        try { Icon = Icon.ExtractAssociatedIcon(Application.ExecutablePath); } catch { }
        Controls.Add(web);
        if (Program.Demo)
        {
            // Cópia de testes: fica fora da área visível, sem barra de tarefas, para não atrapalhar quem está usando o PC.
            StartPosition = FormStartPosition.Manual;
            Location = new Point(-5000, 0);
            ShowInTaskbar = false;
        }
        else if (Program.StartHidden) WindowState = FormWindowState.Minimized;

        engine = new Engine(meta, SettingsStore.Load());
        staticLoad = meta.LoadStaticAsync();

        // Ícone sempre presente na bandeja
        var menu = new ContextMenuStrip();
        menu.Items.Add("Abrir o Analisador", null, (_, _) => RestoreFromTray());
        menu.Items.Add(new ToolStripSeparator());
        menu.Items.Add("Sair", null, (_, _) => ExitApp());
        tray = new NotifyIcon { Icon = Icon ?? SystemIcons.Application, Text = "Analisador LoL", ContextMenuStrip = menu, Visible = true };
        tray.MouseClick += (_, e) => { if (e.Button == MouseButtons.Left) ToggleFromTray(); };

        keyboard = new KeyboardWatcher(() => BeginInvokeSafe(ToggleOverlay));

        engine.StateChanged += json => Post($$"""{"event":"state","data":{{json}}}""");
        engine.Notified += text => Post($$"""{"event":"toast","text":{{JsonSerializer.Serialize(text)}}}""");
        engine.PhaseChanged += phase => BeginInvokeSafe(() => OnPhase(phase));
        ApplyPriority();
        ApplyStartup();
        Load += async (_, _) => await InitWebView();
        HandleCreated += (_, _) => { StartForegroundWatch(); StartShowSignalWatch(); StartWatchdog(); };
        Resize += (_, _) => OnResized();
    }

    // ------------------------------------------------------------------ WebView2

    async Task InitWebView()
    {
        try
        {
            // Sem GPU: a interface é estática e assim não disputa a placa de vídeo com o jogo.
            var opts = new CoreWebView2EnvironmentOptions(
                "--disable-gpu --disable-gpu-compositing --disable-background-networking --disable-features=msSmartScreenProtection,Translate");
            env = await CoreWebView2Environment.CreateAsync(null, Paths.WebView, opts);
            await web.EnsureCoreWebView2Async(env);
        }
        catch (Exception ex)
        {
            Log.Write("WebView2 não iniciou: " + ex.Message);
            MessageBox.Show("Não foi possível iniciar a tela do programa (WebView2).\n\n" + ex.Message, "Analisador LoL", MessageBoxButtons.OK, MessageBoxIcon.Error);
            return;
        }
        var core = web.CoreWebView2;
        var st = core.Settings;
#if DEBUG
        st.AreDevToolsEnabled = true;
#else
        st.AreDevToolsEnabled = false;
        st.AreBrowserAcceleratorKeysEnabled = false;
#endif
        st.AreDefaultContextMenusEnabled = false;
        st.IsStatusBarEnabled = false;
        st.IsZoomControlEnabled = false;
        st.IsPasswordAutosaveEnabled = false;
        st.IsGeneralAutofillEnabled = false;

        core.AddWebResourceRequestedFilter(Host + "*", CoreWebView2WebResourceContext.All);
        core.WebResourceRequested += ServeEmbedded;
        core.WebMessageReceived += OnMessage;
        core.NewWindowRequested += (_, e) => { e.Handled = true; OpenExternal(e.Uri); };
        core.NavigationStarting += (_, e) => { if (!e.Uri.StartsWith(Host)) { e.Cancel = true; OpenExternal(e.Uri); } };
        core.ProcessFailed += OnWebViewFailed;
        env.ProcessInfosChanged += (_, _) => LowerWebViewPriority();
        core.Navigate(Host + "index.html");
        engine.Start();
        if (WindowState == FormWindowState.Minimized) OnResized(); // iniciado escondido: suspende já
    }

    /// <summary>Se o navegador interno cair, recarrega a tela; se cair por inteiro, o app reabre sozinho.</summary>
    void OnWebViewFailed(object? sender, CoreWebView2ProcessFailedEventArgs e)
    {
        Log.Write($"Navegador interno falhou: {e.ProcessFailedKind} ({e.Reason}).");
        if (e.ProcessFailedKind == CoreWebView2ProcessFailedKind.BrowserProcessExited)
        {
            Program.Relaunch("navegador interno fechou");
            ExitApp();
        }
        else if (e.ProcessFailedKind is CoreWebView2ProcessFailedKind.RenderProcessExited or CoreWebView2ProcessFailedKind.RenderProcessUnresponsive)
        {
            try { web.Reload(); } catch { }
        }
    }

    void ServeEmbedded(object? sender, CoreWebView2WebResourceRequestedEventArgs e)
    {
        var path = new Uri(e.Request.Uri).AbsolutePath.TrimStart('/');
        var stream = Assembly.GetExecutingAssembly().GetManifestResourceStream("www/" + path);
        if (stream == null) { e.Response = env!.CreateWebResourceResponse(null, 404, "Not Found", ""); return; }
        var mime = Path.GetExtension(path) switch
        {
            ".html" => "text/html; charset=utf-8",
            ".js" => "text/javascript; charset=utf-8",
            ".css" => "text/css; charset=utf-8",
            ".svg" => "image/svg+xml",
            ".png" => "image/png",
            _ => "application/octet-stream",
        };
        e.Response = env!.CreateWebResourceResponse(stream, 200, "OK", $"Content-Type: {mime}\r\nCache-Control: no-cache");
    }

    // Atualizações de estado podem ser descartadas com a tela congelada (a última é reenviada ao voltar).
    // Respostas a pedidos da tela, não: ficam guardadas e são entregues quando a tela volta.
    readonly Queue<string> pendingReplies = new();

    void Post(string json) => BeginInvokeSafe(() =>
    {
        if (web.CoreWebView2 != null && !suspended) web.CoreWebView2.PostWebMessageAsJson(json);
    });

    void PostReply(string json) => BeginInvokeSafe(() =>
    {
        if (web.CoreWebView2 != null && !suspended) web.CoreWebView2.PostWebMessageAsJson(json);
        else pendingReplies.Enqueue(json);
    });

    void FlushReplies()
    {
        while (pendingReplies.Count > 0 && web.CoreWebView2 != null)
            try { web.CoreWebView2.PostWebMessageAsJson(pendingReplies.Dequeue()); } catch { }
    }

    void BeginInvokeSafe(Action a)
    {
        if (IsDisposed || !IsHandleCreated) return;
        try { BeginInvoke(a); } catch (InvalidOperationException) { }
    }

    async void OnMessage(object? sender, CoreWebView2WebMessageReceivedEventArgs e)
    {
        long id = 0;
        try
        {
            var msg = JsonNode.Parse(e.WebMessageAsJson)!;
            id = msg["id"]?.GetValue<long>() ?? 0;
            var result = await HandleCall(msg["method"]!.GetValue<string>(), msg["params"]);
            PostReply($$"""{"id":{{id}},"result":{{JsonSerializer.Serialize(result, Json.Opts)}}}""");
        }
        catch (Exception ex)
        {
            Log.Write("Mensagem: " + ex.Message);
            PostReply($$"""{"id":{{id}},"error":{{JsonSerializer.Serialize(ex.Message)}}}""");
        }
    }

    async Task<object?> HandleCall(string method, JsonNode? p)
    {
        var s = engine.Settings;
        int Int(string k) => p?[k]?.GetValue<int>() ?? 0;
        string? Str(string k) => p?[k]?.GetValue<string>();

        switch (method)
        {
            case "init":
                try { await staticLoad; }
                catch (Exception ex) { return new { error = ex.Message, settings = s }; }
                await meta.GetTierListAsync(s.Region, s.Tier); // garante o número do patch
                return new
                {
                    @static = meta.ToDto(), settings = s, patch = meta.Patch,
                    state = engine.LastJson.Length > 0 ? JsonNode.Parse(engine.LastJson) : null,
                    version = Application.ProductVersion.Split('+')[0],
                };
            case "tierList":
                return await meta.GetTierListAsync(s.Region, s.Tier);
            case "build":
            {
                var champ = Int("championId");
                if (Str("mode") is { } bm && bm != Mode.Classic && Mode.Supported.Contains(bm))
                    return await meta.GetModeBuildAsync(champ, bm, s.Region);
                var pos = Pos.From(Str("position"));
                if (pos == null)
                {
                    var tier = await meta.GetTierListAsync(s.Region, s.Tier);
                    pos = tier?.Where(t => t.ChampionId == champ).OrderByDescending(t => t.RoleRate).FirstOrDefault()?.Position ?? Pos.Top;
                }
                return await meta.GetBuildAsync(champ, pos, s.Region, s.Tier);
            }
            case "importRunes": return await engine.ImportRunes(Int("championId"), Engine.NormalizePos(Str("position")), Int("index"));
            case "importItemSet": return await engine.ImportItemSet(Int("championId"), Engine.NormalizePos(Str("position")));
            case "applySpells": return await engine.ApplySpells(Int("championId"), Engine.NormalizePos(Str("position")), Int("index"));
            case "benchSwap": return await engine.SwapBench(Int("championId"));
            case "modeList": return await meta.GetModeListAsync(Str("mode") ?? Mode.Aram, s.Region);
            case "setPosition": engine.SetManualPosition(Str("position")); return true;
            case "setSettings":
            {
                var ns = p!["settings"].Deserialize<AppSettings>(Json.Opts) ?? s;
                engine.UpdateSettings(ns);
                ApplyPriority();
                ApplyStartup();
                UpdateHotkey();
                return ns;
            }
            case "clearCache": meta.ClearMetaCache(); engine.Wake(); return true;
            case "openExternal": OpenExternal(Str("url") ?? ""); return true;
            case "hide": HideToTray(); return true;
            default: throw new InvalidOperationException("Método desconhecido: " + method);
        }
    }

    static void OpenExternal(string url)
    {
        if (!url.StartsWith("https://")) return;
        try { Process.Start(new ProcessStartInfo(url) { UseShellExecute = true }); } catch { }
    }

    // ------------------------------------------------------------------ leveza

    void ApplyPriority()
    {
        try { Process.GetCurrentProcess().PriorityClass = engine.Settings.LowPriority ? ProcessPriorityClass.BelowNormal : ProcessPriorityClass.Normal; }
        catch { }
        LowerWebViewPriority();
    }

    /// <summary>Processos do WebView2 também ficam com prioridade abaixo do normal: o jogo sempre ganha a CPU.</summary>
    void LowerWebViewPriority()
    {
        if (env == null) return;
        var prio = engine.Settings.LowPriority ? ProcessPriorityClass.BelowNormal : ProcessPriorityClass.Normal;
        try
        {
            foreach (var info in env.GetProcessInfos())
                try { using var proc = Process.GetProcessById(info.ProcessId); proc.PriorityClass = prio; } catch { }
        }
        catch { }
    }

    /// <summary>Inicia junto com o Windows, já escondido na bandeja (opção nas configurações).</summary>
    void ApplyStartup()
    {
        if (Program.Demo) return; // a cópia de testes não mexe no início com o Windows
        try
        {
            using var run = Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Run", writable: true);
            if (run == null) return;
            if (engine.Settings.StartWithWindows) run.SetValue("AnalisadorLoL", $"\"{Environment.ProcessPath}\" --bandeja");
            else if (run.GetValue("AnalisadorLoL") != null) run.DeleteValue("AnalisadorLoL");
        }
        catch (Exception ex) { Log.Write("Inicialização com o Windows: " + ex.Message); }
    }

    void OnPhase(string phase)
    {
        LowerWebViewPriority();
        var s = engine.Settings;
        if (phase == "ChampSelect" && s.AutoShow && !IsShowing()) RestoreFromTray();
        else if (phase == "InProgress" && s.MinimizeOnGame && IsShowing() && !overlayShown) HideToTray();
    }

    [DllImport("kernel32.dll")] static extern bool SetProcessWorkingSetSize(IntPtr proc, IntPtr min, IntPtr max);

    /// <summary>Devolve ao Windows a memória que não está em uso (usado ao esconder).</summary>
    static void TrimMemory()
    {
        try
        {
            GC.Collect(GC.MaxGeneration, GCCollectionMode.Aggressive, true, true);
            using var me = Process.GetCurrentProcess();
            SetProcessWorkingSetSize(me.Handle, -1, -1);
        }
        catch { }
    }

    bool IsShowing() => Visible && WindowState != FormWindowState.Minimized;

    async void OnResized()
    {
        if (web.CoreWebView2 == null) return;
        if (WindowState == FormWindowState.Minimized && !suspended)
        {
            suspended = true;
            engine.Visible = false;
            overlayShown = false;
            TopMost = false;
            if (engine.Settings.MinimizeToTray) Hide();
            // Escondido: interface congelada e memória reduzida.
            web.Visible = false;
            try
            {
                web.CoreWebView2.MemoryUsageTargetLevel = CoreWebView2MemoryUsageTargetLevel.Low;
                await web.CoreWebView2.TrySuspendAsync();
            }
            catch { }
            TrimMemory();
        }
        else if (WindowState != FormWindowState.Minimized && suspended)
        {
            suspended = false;
            web.Visible = true;
            try
            {
                web.CoreWebView2.Resume();
                web.CoreWebView2.MemoryUsageTargetLevel = CoreWebView2MemoryUsageTargetLevel.Normal;
            }
            catch { }
            FlushReplies();
            if (engine.LastJson.Length > 0) Post($$"""{"event":"state","data":{{engine.LastJson}}}""");
            engine.Visible = true;
        }
    }

    void RestoreFromTray()
    {
        if (!overlayShown) TopMost = false;
        Show();
        if (WindowState == FormWindowState.Minimized) WindowState = FormWindowState.Normal;
        Activate();
    }

    void HideToTray()
    {
        overlayShown = false;
        TopMost = false;
        if (WindowState != FormWindowState.Minimized) WindowState = FormWindowState.Minimized;
        Hide();
    }

    void ToggleFromTray()
    {
        if (IsShowing()) HideToTray(); else RestoreFromTray();
    }

    void ExitApp()
    {
        exiting = true;
        tray.Visible = false;
        Close();
    }

    // O X da janela só esconde: o app continua na bandeja, pronto para o atalho.
    protected override bool ShowWithoutActivation => Program.Demo;

    protected override void OnFormClosing(FormClosingEventArgs e)
    {
        if (!exiting && e.CloseReason == CloseReason.UserClosing)
        {
            e.Cancel = true;
            HideToTray();
            if (!trayHintShown)
            {
                trayHintShown = true;
                var key = engine.Settings.OverlayHotkey;
                tray.ShowBalloonTip(4000, "O Analisador continua aberto",
                    string.IsNullOrEmpty(key) ? "Ele fica aqui na bandeja. Clique no ícone para abrir; para fechar de vez, use \"Sair\"."
                        : $"Ele fica aqui na bandeja. {key} no jogo ou um clique no ícone abre de novo; para fechar de vez, use \"Sair\".", ToolTipIcon.Info);
            }
            return;
        }
        base.OnFormClosing(e);
    }

    // ------------------------------------------------------------------ "abrir" vindo de outra cópia (busca do Windows, atalho)

    void StartShowSignalWatch()
    {
        var ev = new EventWaitHandle(false, EventResetMode.AutoReset, Program.ShowEventName);
        new Thread(() =>
        {
            while (true)
            {
                ev.WaitOne(); // parado, sem gastar CPU, até alguém abrir o app de novo
                BeginInvokeSafe(RestoreFromTray);
            }
        }) { IsBackground = true, Name = "Pedido para abrir" }.Start();
    }

    // ------------------------------------------------------------------ vigia: se a tela travar, o app reabre sozinho

    void StartWatchdog()
    {
        new Thread(() =>
        {
            int misses = 0;
            while (!IsDisposed)
            {
                Thread.Sleep(15000);
                var pong = new ManualResetEventSlim(); // sem "using": a tela pode responder depois
                try { BeginInvoke(() => pong.Set()); } catch { return; }
                // Duas falhas seguidas (≈1 min parado) = travado. Uma só pode ser o PC voltando da suspensão.
                misses = pong.Wait(30000) ? 0 : misses + 1;
                if (misses < 2) continue;
                Log.Write("A tela do app travou: reabrindo sozinho.");
                Program.Relaunch("tela travada");
                try { Process.GetCurrentProcess().Kill(); } catch { }
            }
        }) { IsBackground = true, Name = "Vigia", Priority = ThreadPriority.BelowNormal }.Start();
    }

    // ------------------------------------------------------------------ sobreposição (atalho no jogo)

    const uint EVENT_SYSTEM_FOREGROUND = 3, WINEVENT_OUTOFCONTEXT = 0;
    delegate void WinEventProc(IntPtr hook, uint ev, IntPtr hwnd, int idObject, int idChild, uint thread, uint time);
    [DllImport("user32.dll")] static extern IntPtr SetWinEventHook(uint min, uint max, IntPtr mod, WinEventProc proc, uint pid, uint thread, uint flags);
    [DllImport("user32.dll")] static extern bool UnhookWinEvent(IntPtr hook);
    [DllImport("user32.dll")] static extern IntPtr GetForegroundWindow();
    [DllImport("user32.dll")] static extern bool SetForegroundWindow(IntPtr hwnd);
    [DllImport("user32.dll")] static extern uint GetWindowThreadProcessId(IntPtr hwnd, out uint pid);

    WinEventProc? foregroundProc; // campo: o coletor de lixo não pode liberar o callback
    IntPtr foregroundHook;
    static readonly HashSet<string> LeagueProcesses = new(StringComparer.OrdinalIgnoreCase)
        { "League of Legends", "LeagueClientUx", "LeagueClientUxRender", "LeagueClient", "AnalisadorLoL" };

    void StartForegroundWatch()
    {
        // Evento do Windows a cada troca de janela em primeiro plano: nenhuma consulta periódica.
        foregroundProc = (_, _, hwnd, _, _, _, _) => UpdateHotkey(hwnd);
        foregroundHook = SetWinEventHook(EVENT_SYSTEM_FOREGROUND, EVENT_SYSTEM_FOREGROUND, IntPtr.Zero, foregroundProc, 0, 0, WINEVENT_OUTOFCONTEXT);
        UpdateHotkey();
    }

    static string? ProcessOf(IntPtr hwnd)
    {
        try
        {
            GetWindowThreadProcessId(hwnd, out var pid);
            using var p = Process.GetProcessById((int)pid);
            return p.ProcessName;
        }
        catch { return null; }
    }

    /// <summary>O atalho só vale com o LoL (jogo ou cliente) ou o app em primeiro plano.</summary>
    void UpdateHotkey(IntPtr fg = default)
    {
        if (fg == IntPtr.Zero) fg = GetForegroundWindow();
        var name = ProcessOf(fg);
        if (name == "League of Legends") gameWindow = fg;
        var active = name != null && LeagueProcesses.Contains(name);
        keyboard.Set(active, KeyboardWatcher.Parse(engine.Settings.OverlayHotkey));
    }

    /// <summary>
    /// Mostra o app por cima do jogo (sem roubar o teclado do jogo) ou esconde de novo.
    /// Nada aqui espera pelo jogo: só comandos rápidos de janela, para a tela nunca travar.
    /// </summary>
    void ToggleOverlay()
    {
        var showing = IsShowing() && (overlayShown || GetForegroundWindow() == Handle);
        if (showing)
        {
            HideToTray();
            if (gameWindow != IntPtr.Zero) SetForegroundWindow(gameWindow);
        }
        else
        {
            overlayShown = true;
            Show();
            if (WindowState == FormWindowState.Minimized) WindowState = FormWindowState.Normal;
            TopMost = true; // por cima do jogo (modos "Sem bordas" e "Janela")
        }
    }

    // Barra de título escura
    [DllImport("dwmapi.dll")] static extern int DwmSetWindowAttribute(IntPtr hwnd, int attr, ref int value, int size);
    protected override void OnHandleCreated(EventArgs e)
    {
        base.OnHandleCreated(e);
        int on = 1;
        DwmSetWindowAttribute(Handle, 20, ref on, sizeof(int));
    }

    protected override void OnFormClosed(FormClosedEventArgs e)
    {
        tray.Visible = false;
        tray.Dispose();
        keyboard.Dispose();
        if (foregroundHook != IntPtr.Zero) UnhookWinEvent(foregroundHook);
        engine.Dispose();
        meta.Dispose();
        base.OnFormClosed(e);
    }
}
