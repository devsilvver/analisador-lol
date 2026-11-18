using System.Runtime.InteropServices;

namespace AnalisadorLoL;

/// <summary>
/// Atalho de teclado que funciona com o LoL em foco.
///
/// O jogo lê o teclado em "raw input" e bloqueia os atalhos comuns do Windows (RegisterHotKey), então usamos
/// um gancho de teclado de baixo nível. Ele roda numa linha de execução própria, separada da tela do app:
/// se a tela estiver ocupada, as teclas do jogo nunca atrasam. O gancho só é instalado enquanto o LoL ou o app
/// estão em primeiro plano, e só reage à combinação escolhida (todas as outras teclas passam direto).
/// </summary>
public sealed class KeyboardWatcher : IDisposable
{
    const int WH_KEYBOARD_LL = 13, WM_KEYDOWN = 0x100, WM_SYSKEYDOWN = 0x104, WM_QUIT = 0x12;
    const uint MSG_INSTALL = 0x8001, MSG_REMOVE = 0x8002;
    public const uint MOD_ALT = 1, MOD_CONTROL = 2, MOD_SHIFT = 4;

    delegate IntPtr LowLevelKeyboardProc(int nCode, IntPtr wParam, IntPtr lParam);
    [StructLayout(LayoutKind.Sequential)] struct KBDLLHOOKSTRUCT { public uint vkCode, scanCode, flags, time; public IntPtr extra; }
    [StructLayout(LayoutKind.Sequential)] struct MSG { public IntPtr hwnd; public uint message; public IntPtr wParam, lParam; public uint time; public int x, y; }

    [DllImport("user32.dll", SetLastError = true)] static extern IntPtr SetWindowsHookEx(int idHook, LowLevelKeyboardProc fn, IntPtr hMod, uint threadId);
    [DllImport("user32.dll")] static extern bool UnhookWindowsHookEx(IntPtr hhk);
    [DllImport("user32.dll")] static extern IntPtr CallNextHookEx(IntPtr hhk, int nCode, IntPtr wParam, IntPtr lParam);
    [DllImport("user32.dll")] static extern short GetAsyncKeyState(int vk);
    [DllImport("user32.dll")] static extern int GetMessage(out MSG msg, IntPtr hwnd, uint min, uint max);
    [DllImport("user32.dll")] static extern bool PeekMessage(out MSG msg, IntPtr hwnd, uint min, uint max, uint remove);
    [DllImport("user32.dll")] static extern bool PostThreadMessage(uint threadId, uint msg, IntPtr wParam, IntPtr lParam);
    [DllImport("kernel32.dll")] static extern uint GetCurrentThreadId();
    [DllImport("kernel32.dll", CharSet = CharSet.Unicode)] static extern IntPtr GetModuleHandle(string? name);

    readonly Action onHotkey;
    readonly LowLevelKeyboardProc proc; // campo: o coletor de lixo não pode liberar o callback
    readonly Thread thread;
    readonly ManualResetEventSlim ready = new();
    uint threadId;
    IntPtr hook;
    volatile bool held, wanted;
    (uint mods, uint vk)? key;

    public KeyboardWatcher(Action onHotkey)
    {
        this.onHotkey = onHotkey;
        proc = Callback;
        thread = new Thread(Run) { IsBackground = true, Name = "Atalho de teclado", Priority = ThreadPriority.Highest };
        thread.Start();
        ready.Wait(2000);
    }

    public static (uint mods, uint vk)? Parse(string? s) => s switch
    {
        "Ctrl+X" => (MOD_CONTROL, (uint)Keys.X),
        "Ctrl+Shift+X" => (MOD_CONTROL | MOD_SHIFT, (uint)Keys.X),
        "Alt+X" => (MOD_ALT, (uint)Keys.X),
        "F10" => (0u, (uint)Keys.F10),
        _ => null,
    };

    /// <summary>Liga ou desliga o gancho. Pode ser chamado de qualquer linha de execução.</summary>
    public void Set(bool active, (uint mods, uint vk)? combo)
    {
        key = combo;
        wanted = active && combo != null; // guardado: se a linha ainda estiver nascendo, ela aplica ao ficar pronta
        if (threadId != 0) PostThreadMessage(threadId, wanted ? MSG_INSTALL : MSG_REMOVE, IntPtr.Zero, IntPtr.Zero);
    }

    void Run()
    {
        threadId = GetCurrentThreadId();
        PeekMessage(out _, IntPtr.Zero, 0, 0, 0); // cria a fila de mensagens desta linha
        ready.Set();
        if (wanted) PostThreadMessage(threadId, MSG_INSTALL, IntPtr.Zero, IntPtr.Zero);
        while (GetMessage(out var msg, IntPtr.Zero, 0, 0) > 0)
        {
            if (msg.message == MSG_INSTALL && hook == IntPtr.Zero)
            {
                hook = SetWindowsHookEx(WH_KEYBOARD_LL, proc, GetModuleHandle(null), 0);
                if (hook == IntPtr.Zero) Log.Write("Atalho: gancho de teclado recusado (erro " + Marshal.GetLastWin32Error() + ").");
            }
            else if (msg.message == MSG_REMOVE && hook != IntPtr.Zero)
            {
                UnhookWindowsHookEx(hook);
                hook = IntPtr.Zero;
                held = false;
            }
        }
        if (hook != IntPtr.Zero) UnhookWindowsHookEx(hook);
    }

    static bool Down(int vk) => (GetAsyncKeyState(vk) & 0x8000) != 0;

    IntPtr Callback(int nCode, IntPtr wParam, IntPtr lParam)
    {
        if (nCode >= 0 && key is { } k)
        {
            var info = Marshal.PtrToStructure<KBDLLHOOKSTRUCT>(lParam);
            if (info.vkCode == k.vk)
            {
                var isDown = (int)wParam is WM_KEYDOWN or WM_SYSKEYDOWN;
                if (!isDown) held = false;
                else if (held) return 1; // tecla segurada: ignora as repetições
                else if (Down(0x11) == ((k.mods & MOD_CONTROL) != 0) && Down(0x10) == ((k.mods & MOD_SHIFT) != 0)
                         && Down(0x12) == ((k.mods & MOD_ALT) != 0))
                {
                    held = true;
                    try { onHotkey(); } catch { }
                    return 1; // o jogo não recebe o atalho
                }
            }
        }
        return CallNextHookEx(hook, nCode, wParam, lParam);
    }

    public void Dispose()
    {
        if (threadId != 0) PostThreadMessage(threadId, WM_QUIT, IntPtr.Zero, IntPtr.Zero);
    }
}
