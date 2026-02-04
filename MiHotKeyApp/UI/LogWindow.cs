namespace MiHotKeyApp.UI;

using Microsoft.Extensions.Logging;

internal sealed class LogWindow : Form
{
    private readonly ComboBox _level;
    private readonly CheckBox _autoScroll;
    private readonly TextBox _text;
    private readonly Button _refresh;
    private readonly Button _copyAll;

    public LogWindow()
    {
        Text = "MiHotKey — Log (last 100)";
        Width = 900;
        Height = 550;
        StartPosition = FormStartPosition.CenterScreen;

        var panel = new FlowLayoutPanel
        {
            Dock = DockStyle.Top,
            Height = 34,
            FlowDirection = FlowDirection.LeftToRight,
            WrapContents = false,
            Padding = new Padding(6, 6, 6, 0),
        };

        _level = new ComboBox
        {
            DropDownStyle = ComboBoxStyle.DropDownList,
            Width = 130,
        };
        _level.Items.AddRange(new object[]
        {
            LogLevel.Trace,
            LogLevel.Debug,
            LogLevel.Information,
            LogLevel.Warning,
            LogLevel.Error,
            LogLevel.Critical,
        });
        _level.SelectedItem = LogLevel.Information;

        _autoScroll = new CheckBox
        {
            Text = "Auto-scroll",
            Checked = true,
            AutoSize = true,
            Margin = new Padding(12, 6, 0, 0),
        };

        _refresh = new Button { Text = "Refresh", AutoSize = true, Margin = new Padding(12, 3, 0, 0) };
        _copyAll = new Button { Text = "Copy all", AutoSize = true, Margin = new Padding(6, 3, 0, 0) };

        panel.Controls.Add(new Label { Text = "Level:", AutoSize = true, Margin = new Padding(0, 8, 0, 0) });
        panel.Controls.Add(_level);
        panel.Controls.Add(_autoScroll);
        panel.Controls.Add(_refresh);
        panel.Controls.Add(_copyAll);

        _text = new TextBox
        {
            Dock = DockStyle.Fill,
            Multiline = true,
            ReadOnly = true,
            ScrollBars = ScrollBars.Both,
            WordWrap = false,
            Font = new System.Drawing.Font("Consolas", 9f),
        };

        Controls.Add(_text);
        Controls.Add(panel);

        _copyAll.Click += (_, __) => CopyAll();
    }

    public event Action? RefreshRequested;
    public event Action<LogLevel>? LevelChanged;

    public LogLevel MinLevel => (LogLevel)(_level.SelectedItem ?? LogLevel.Information);
    public bool AutoScrollEnabled => _autoScroll.Checked;

    protected override void OnHandleCreated(EventArgs e)
    {
        base.OnHandleCreated(e);
        _refresh.Click += (_, __) => RefreshRequested?.Invoke();
        _level.SelectedValueChanged += (_, __) => LevelChanged?.Invoke(MinLevel);
    }

    public void SetText(string text)
    {
        _text.Text = text;
        if (AutoScrollEnabled)
        {
            _text.SelectionStart = _text.TextLength;
            _text.ScrollToCaret();
        }
    }

    private void CopyAll()
    {
        try
        {
            Clipboard.SetText(_text.Text);
        }
        catch
        {
        }
    }
}
