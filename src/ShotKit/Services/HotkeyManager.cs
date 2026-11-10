using System.Windows.Input;
using System.Windows.Interop;
using static ShotKit.Native.NativeMethods;

namespace ShotKit.Services;

/// <summary>Registra atalhos globais (RegisterHotKey) numa janela de mensagens oculta.</summary>
public sealed class HotkeyManager : IDisposable
{
    private readonly HwndSource _source;
    private readonly Dictionary<int, HotkeyAction> _registered = new();
    private int _nextId = 0x5000;

    public event Action<HotkeyAction>? Triggered;

    public HotkeyManager()
    {
        // Janela message-only (HWND_MESSAGE = -3): invisível e sem entrada na barra de tarefas.
        var p = new HwndSourceParameters("ShotKitHotkeys") { ParentWindow = new IntPtr(-3), WindowStyle = 0 };
        _source = new HwndSource(p);
        _source.AddHook(WndProc);
    }

    /// <summary>Registra a lista e retorna os atalhos que falharam (já em uso por outro app).</summary>
    public IReadOnlyList<HotkeySetting> RegisterAll(IEnumerable<HotkeySetting> hotkeys)
    {
        UnregisterAll();
        var failed = new List<HotkeySetting>();
        foreach (var hk in hotkeys)
        {
            if (hk.IsEmpty) continue;
            int id = _nextId++;
            uint vk = (uint)KeyInterop.VirtualKeyFromKey(hk.Key);
            if (RegisterHotKey(_source.Handle, id, ToNative(hk.Modifiers) | MOD_NOREPEAT, vk))
            {
                _registered[id] = hk.Action;
            }
            else
            {
                failed.Add(hk);
                Log.Info($"Atalho em uso, não registrado: {hk}");
            }
        }
        return failed;
    }

    public void UnregisterAll()
    {
        foreach (int id in _registered.Keys) UnregisterHotKey(_source.Handle, id);
        _registered.Clear();
    }

    private static uint ToNative(ModifierKeys m)
    {
        uint r = 0;
        if (m.HasFlag(ModifierKeys.Alt)) r |= 0x1;
        if (m.HasFlag(ModifierKeys.Control)) r |= 0x2;
        if (m.HasFlag(ModifierKeys.Shift)) r |= 0x4;
        if (m.HasFlag(ModifierKeys.Windows)) r |= 0x8;
        return r;
    }

    private IntPtr WndProc(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        if (msg == WM_HOTKEY && _registered.TryGetValue(wParam.ToInt32(), out var action))
        {
            handled = true;
            Triggered?.Invoke(action);
        }
        return IntPtr.Zero;
    }

    public void Dispose()
    {
        UnregisterAll();
        _source.RemoveHook(WndProc);
        _source.Dispose();
    }
}
