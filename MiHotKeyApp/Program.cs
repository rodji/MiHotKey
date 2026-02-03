namespace MiHotKeyApp;

using System.Runtime.InteropServices;
using System.Windows.Forms;

internal static class Program
{
    [STAThread]
    static void Main()
    {
        ApplicationConfiguration.Initialize();

        using var tray = new NotifyIcon
        {
            Icon = System.Drawing.SystemIcons.Application,
            Visible = true,
            Text = "Mic Mute Router"
        };

        var menu = new ContextMenuStrip();
        menu.Items.Add("Exit", null, (_, __) => Application.Exit());
        tray.ContextMenuStrip = menu;

        using var host = new HotkeyHostForm();
        host.ShowInTaskbar = false;
        host.WindowState = FormWindowState.Minimized;
        host.Load += (_, __) => host.Hide();

        Application.Run();
    }
}

internal sealed class HotkeyHostForm : Form
{
    private const int WM_HOTKEY = 0x0312;

    protected override void OnHandleCreated(EventArgs e)
    {
        base.OnHandleCreated(e);
        // пример: Ctrl+Alt+M
        RegisterHotKey(this.Handle, 1, MOD_CONTROL | MOD_ALT, (int)Keys.M);
    }

    protected override void OnFormClosed(FormClosedEventArgs e)
    {
        UnregisterHotKey(this.Handle, 1);
        base.OnFormClosed(e);
    }

    protected override void WndProc(ref Message m)
    {
        if (m.Msg == WM_HOTKEY)
        {
            // TODO: тут вызываешь выбор окна + отправку комбинации
        }
        base.WndProc(ref m);
    }

    private const int MOD_ALT = 0x0001;
    private const int MOD_CONTROL = 0x0002;

    [DllImport("user32.dll")]
    private static extern bool RegisterHotKey(nint hWnd, int id, int fsModifiers, int vk);

    [DllImport("user32.dll")]
    private static extern bool UnregisterHotKey(nint hWnd, int id);
}
