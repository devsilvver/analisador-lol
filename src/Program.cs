using System.Diagnostics;

namespace AnalisadorLoL;

static class Program
{
    public const string ShowEventName = "AnalisadorLoL_mostrar";
    /// <summary>Iniciado pelo Windows ou reiniciado sozinho: começa escondido na bandeja.</summary>
    public static bool StartHidden { get; private set; }
    static readonly DateTime Started = DateTime.Now;
    static int relaunching;

    [STAThread]
    static void Main(string[] args)
    {
        StartHidden = args.Contains("--bandeja");
        var restarted = args.Contains("--reiniciado");

        var mutex = new Mutex(false, "AnalisadorLoL_instancia_unica");
        bool owned;
        // Reinício automático: espera a cópia antiga terminar de fechar.
        try { owned = mutex.WaitOne(restarted ? 15000 : 0); }
        catch (AbandonedMutexException) { owned = true; }
        if (!owned)
        {
            // Já está aberto (talvez escondido na bandeja): só pede para a cópia aberta aparecer.
            try { using var ev = EventWaitHandle.OpenExisting(ShowEventName); ev.Set(); } catch { }
            return;
        }

        Application.SetUnhandledExceptionMode(UnhandledExceptionMode.CatchException);
        Application.ThreadException += (_, e) => Log.Write("Erro na tela (o app continua): " + e.Exception);
        TaskScheduler.UnobservedTaskException += (_, e) => { Log.Write("Erro em tarefa: " + e.Exception.GetBaseException().Message); e.SetObserved(); };
        AppDomain.CurrentDomain.UnhandledException += (_, e) =>
        {
            Log.Write("Erro fatal: " + e.ExceptionObject);
            Relaunch("erro fatal");
        };

        if (restarted) Log.Write("App reaberto sozinho na bandeja.");
        ApplicationConfiguration.Initialize();
        Application.Run(new MainForm());
        GC.KeepAlive(mutex);
    }

    /// <summary>Abre uma cópia nova escondida na bandeja (a atual está para fechar).</summary>
    public static void Relaunch(string reason)
    {
        if (Interlocked.Exchange(ref relaunching, 1) == 1) return;
        // Evita ciclo de reinícios: se caiu logo depois de abrir, não insiste.
        if (DateTime.Now - Started < TimeSpan.FromMinutes(1)) { Log.Write($"Não reaberto ({reason}): caiu menos de 1 minuto após abrir."); return; }
        try
        {
            Log.Write($"Reabrindo sozinho: {reason}.");
            Process.Start(new ProcessStartInfo(Environment.ProcessPath!, "--bandeja --reiniciado") { UseShellExecute = false });
        }
        catch (Exception ex) { Log.Write("Falha ao reabrir: " + ex.Message); }
    }
}
