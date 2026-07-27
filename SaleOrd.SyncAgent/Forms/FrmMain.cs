using System.Reflection;
using Microsoft.Win32;
using SaleOrd.SyncAgent.Models;
using SaleOrd.SyncAgent.Services;

namespace SaleOrd.SyncAgent.Forms;

public class FrmMain : Form
{
    private readonly ConfigService _configSvc = new();
    private readonly AgentLogger _logger;
    private readonly SyncOrchestrator _orchestrator;
    private AgentConfig _config = new();
    private bool _reallyExit;

    // Status tab controls
    private Label _lblSqlStatus = new();
    private Label _lblTallyStatus = new();
    private Label _lblLastMaster = new();
    private Label _lblNextMaster = new();
    private Label _lblLastOrder = new();
    private Label _lblNextOrder = new();
    private Label _lblRunState = new();
    private Button _btnStartStop = new();
    private Button _btnSyncNow = new();

    // Config tab controls
    private TextBox _txtSqlServer = new();
    private TextBox _txtSqlDatabase = new();
    private TextBox _txtSqlUser = new();
    private TextBox _txtSqlPassword = new();
    private CheckBox _chkEncrypt = new();
    private TextBox _txtTallyUrl = new();
    private NumericUpDown _numMasterInterval = new();
    private NumericUpDown _numOrderInterval = new();
    private CheckBox _chkStartMinimized = new();
    private CheckBox _chkAutoStart = new();
    private Label _lblConfigMsg = new();

    // Logs tab
    private TextBox _txtLogs = new();

    private NotifyIcon _trayIcon = new();

    public FrmMain()
    {
        _logger = new AgentLogger(ConfigService.LogFilePath);
        _orchestrator = new SyncOrchestrator(_logger);

        var version = Assembly.GetExecutingAssembly().GetName().Version?.ToString(3) ?? "1.0.0";
        Text = $"SaleOrd Sync Agent — v{version}";
        Width = 720;
        Height = 560;
        StartPosition = FormStartPosition.CenterScreen;
        MinimumSize = new Size(640, 480);

        try { Icon = new Icon(Path.Combine(AppContext.BaseDirectory, "agent.ico")); }
        catch { /* fall back to the default form icon if the file isn't next to the exe for some reason */ }

        BuildUi();
        BuildTrayIcon();

        _logger.LogWritten += (_, e) => AppendLogLine(e);
        _orchestrator.StatusChanged += (_, e) => UpdateStatus(e);

        Load += FrmMain_Load;
        FormClosing += FrmMain_FormClosing;
        Resize += FrmMain_Resize;
    }

    private void FrmMain_Load(object? sender, EventArgs e)
    {
        _config = _configSvc.Load();
        PopulateConfigFields();

        if (!string.IsNullOrWhiteSpace(_config.SqlServer) && !string.IsNullOrWhiteSpace(_config.TallyUrl))
        {
            _orchestrator.Start(_config);
            _btnStartStop.Text = "Stop Agent";
        }

        if (_config.StartMinimized)
        {
            WindowState = FormWindowState.Minimized;
            Hide();
        }
    }

    // ── UI construction ──────────────────────────────────────────────────

    private void BuildUi()
    {
        var tabs = new TabControl { Dock = DockStyle.Fill };

        var tabStatus = new TabPage("Status");
        var tabConfig = new TabPage("Configuration");
        var tabLogs = new TabPage("Logs");

        BuildStatusTab(tabStatus);
        BuildConfigTab(tabConfig);
        BuildLogsTab(tabLogs);

        tabs.TabPages.Add(tabStatus);
        tabs.TabPages.Add(tabConfig);
        tabs.TabPages.Add(tabLogs);

        Controls.Add(tabs);
    }

    private void BuildStatusTab(TabPage page)
    {
        var panel = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 3,
            Padding = new Padding(16),
            AutoSize = true
        };
        panel.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 40));
        panel.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 40));
        panel.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 20));

        var btnTestSql = new Button { Text = "Test SQL", AutoSize = true };
        btnTestSql.Click += async (_, _) => await TestSqlClicked();

        var btnTestTally = new Button { Text = "Test Tally", AutoSize = true };
        btnTestTally.Click += async (_, _) => await TestTallyClicked();

        _lblSqlStatus.Text = "SQL: unknown";
        _lblSqlStatus.AutoSize = true;
        _lblTallyStatus.Text = "Tally: unknown";
        _lblTallyStatus.AutoSize = true;

        _lblLastMaster.Text = "Last master sync: never";
        _lblLastMaster.AutoSize = true;
        _lblNextMaster.Text = "Next master sync: —";
        _lblNextMaster.AutoSize = true;
        _lblLastOrder.Text = "Last order cycle: never";
        _lblLastOrder.AutoSize = true;
        _lblNextOrder.Text = "Next order cycle: —";
        _lblNextOrder.AutoSize = true;

        _lblRunState.Text = "Agent: stopped";
        _lblRunState.AutoSize = true;
        _lblRunState.Font = new Font(_lblRunState.Font, FontStyle.Bold);

        _btnStartStop.Text = "Start Agent";
        _btnStartStop.AutoSize = true;
        _btnStartStop.Click += BtnStartStop_Click;

        _btnSyncNow.Text = "Sync Now";
        _btnSyncNow.AutoSize = true;
        _btnSyncNow.Click += async (_, _) => await SyncNowClicked();

        var lblLogPath = new Label
        {
            Text = "Log file: " + ConfigService.LogFilePath,
            AutoSize = true,
            ForeColor = Color.Gray,
            Font = new Font(_lblRunState.Font.FontFamily, 8)
        };

        int row = 0;
        panel.RowCount = 9;
        panel.Controls.Add(_lblRunState, 0, row); panel.Controls.Add(_btnStartStop, 1, row); panel.Controls.Add(_btnSyncNow, 2, row); row++;
        panel.Controls.Add(new Label { Text = "", Height = 10 }, 0, row); row++;
        panel.Controls.Add(_lblSqlStatus, 0, row); panel.Controls.Add(btnTestSql, 1, row); row++;
        panel.Controls.Add(_lblTallyStatus, 0, row); panel.Controls.Add(btnTestTally, 1, row); row++;
        panel.Controls.Add(new Label { Text = "", Height = 10 }, 0, row); row++;
        panel.Controls.Add(_lblLastMaster, 0, row); panel.Controls.Add(_lblNextMaster, 1, row); row++;
        panel.Controls.Add(_lblLastOrder, 0, row); panel.Controls.Add(_lblNextOrder, 1, row); row++;
        panel.Controls.Add(new Label { Text = "", Height = 10 }, 0, row); row++;
        panel.Controls.Add(lblLogPath, 0, row); panel.SetColumnSpan(lblLogPath, 3); row++;

        page.Controls.Add(panel);
    }

    private void BuildConfigTab(TabPage page)
    {
        var panel = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 2,
            Padding = new Padding(16),
            AutoSize = true
        };
        panel.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 180));
        panel.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));

        void AddRow(string label, Control control)
        {
            var r = panel.RowCount;
            panel.RowCount = r + 1;
            control.Dock = DockStyle.Fill;
            panel.Controls.Add(new Label { Text = label, AutoSize = true, TextAlign = ContentAlignment.MiddleLeft, Anchor = AnchorStyles.Left }, 0, r);
            panel.Controls.Add(control, 1, r);
        }

        _txtSqlServer.Width = 320;
        _txtSqlDatabase.Width = 320;
        _txtSqlUser.Width = 320;
        _txtSqlPassword.Width = 320;
        _txtSqlPassword.UseSystemPasswordChar = true;
        _txtTallyUrl.Width = 320;

        _numMasterInterval.Minimum = 1;
        _numMasterInterval.Maximum = 1440;
        _numMasterInterval.Value = 30;

        _numOrderInterval.Minimum = 1;
        _numOrderInterval.Maximum = 1440;
        _numOrderInterval.Value = 3;

        AddRow("SQL Server (localhost or remote)", _txtSqlServer);
        AddRow("Database", _txtSqlDatabase);
        AddRow("SQL User", _txtSqlUser);
        AddRow("SQL Password", _txtSqlPassword);
        AddRow("Encrypt connection", _chkEncrypt);
        AddRow("Local Tally URL", _txtTallyUrl);
        AddRow("Master sync interval (min)", _numMasterInterval);
        AddRow("Order push interval (min)", _numOrderInterval);
        AddRow("Start minimized to tray", _chkStartMinimized);
        AddRow("Start with Windows", _chkAutoStart);

        var btnSave = new Button { Text = "Save && Apply", AutoSize = true };
        btnSave.Click += BtnSaveConfig_Click;

        _lblConfigMsg.AutoSize = true;
        _lblConfigMsg.ForeColor = Color.Green;

        var r2 = panel.RowCount;
        panel.RowCount = r2 + 1;
        panel.Controls.Add(btnSave, 1, r2);
        var r3 = panel.RowCount;
        panel.RowCount = r3 + 1;
        panel.Controls.Add(_lblConfigMsg, 1, r3);

        page.Controls.Add(panel);
    }

    private void BuildLogsTab(TabPage page)
    {
        _txtLogs.Multiline = true;
        _txtLogs.ReadOnly = true;
        _txtLogs.ScrollBars = ScrollBars.Vertical;
        _txtLogs.Dock = DockStyle.Fill;
        _txtLogs.Font = new Font("Consolas", 9);
        _txtLogs.WordWrap = false;

        var btnClear = new Button { Text = "Clear", Dock = DockStyle.Top, AutoSize = true };
        btnClear.Click += (_, _) => _txtLogs.Clear();

        page.Controls.Add(_txtLogs);
        page.Controls.Add(btnClear);
    }

    private void BuildTrayIcon()
    {
        _trayIcon.Icon = Icon ?? SystemIcons.Application;
        _trayIcon.Text = "SaleOrd Sync Agent";
        _trayIcon.Visible = true;

        var menu = new ContextMenuStrip();
        menu.Items.Add("Open", null, (_, _) => RestoreFromTray());
        menu.Items.Add("Sync Now", null, async (_, _) => await SyncNowClicked());
        menu.Items.Add(new ToolStripSeparator());
        menu.Items.Add("Exit", null, (_, _) => { _reallyExit = true; Close(); });
        _trayIcon.ContextMenuStrip = menu;
        _trayIcon.DoubleClick += (_, _) => RestoreFromTray();
    }

    private void RestoreFromTray()
    {
        Show();
        WindowState = FormWindowState.Normal;
        Activate();
    }

    private void FrmMain_Resize(object? sender, EventArgs e)
    {
        if (WindowState == FormWindowState.Minimized) Hide();
    }

    private void FrmMain_FormClosing(object? sender, FormClosingEventArgs e)
    {
        if (_reallyExit) return;
        // Closing the window minimizes to tray instead of exiting — this is a
        // background agent, accidental close shouldn't stop the sync cycle.
        e.Cancel = true;
        WindowState = FormWindowState.Minimized;
        Hide();
    }

    // ── Actions ──────────────────────────────────────────────────────────

    private void PopulateConfigFields()
    {
        _txtSqlServer.Text = _config.SqlServer;
        _txtSqlDatabase.Text = _config.SqlDatabase;
        _txtSqlUser.Text = _config.SqlUser;
        _txtSqlPassword.Text = ConfigService.UnprotectPassword(_config.SqlPasswordProtected);
        _chkEncrypt.Checked = _config.SqlEncrypt;
        _txtTallyUrl.Text = _config.TallyUrl;
        _numMasterInterval.Value = Math.Clamp(_config.MasterSyncIntervalMinutes, 1, 1440);
        _numOrderInterval.Value = Math.Clamp(_config.OrderPushIntervalMinutes, 1, 1440);
        _chkStartMinimized.Checked = _config.StartMinimized;
        _chkAutoStart.Checked = _config.AutoStartWithWindows;
    }

    private void BtnSaveConfig_Click(object? sender, EventArgs e)
    {
        _config.SqlServer = _txtSqlServer.Text.Trim();
        _config.SqlDatabase = _txtSqlDatabase.Text.Trim();
        _config.SqlUser = _txtSqlUser.Text.Trim();
        _config.SqlPasswordProtected = ConfigService.ProtectPassword(_txtSqlPassword.Text);
        _config.SqlEncrypt = _chkEncrypt.Checked;
        _config.TallyUrl = _txtTallyUrl.Text.Trim();
        _config.MasterSyncIntervalMinutes = (int)_numMasterInterval.Value;
        _config.OrderPushIntervalMinutes = (int)_numOrderInterval.Value;
        _config.StartMinimized = _chkStartMinimized.Checked;
        _config.AutoStartWithWindows = _chkAutoStart.Checked;

        _configSvc.Save(_config);
        SetAutoStart(_config.AutoStartWithWindows);

        _orchestrator.Stop();
        _orchestrator.Start(_config);
        _btnStartStop.Text = "Stop Agent";

        _lblConfigMsg.ForeColor = Color.Green;
        _lblConfigMsg.Text = $"Saved and applied at {DateTime.Now:HH:mm:ss}.";
        _logger.Info("Configuration saved and agent restarted with new settings.");
    }

    private void BtnStartStop_Click(object? sender, EventArgs e)
    {
        if (_orchestrator.IsRunning)
        {
            _orchestrator.Stop();
            _btnStartStop.Text = "Start Agent";
        }
        else
        {
            _orchestrator.Start(_config);
            _btnStartStop.Text = "Stop Agent";
        }
    }

    private async Task SyncNowClicked()
    {
        _btnSyncNow.Enabled = false;
        try { await _orchestrator.RunNowAsync(); }
        finally { _btnSyncNow.Enabled = true; }
    }

    private async Task TestSqlClicked()
    {
        var cfg = CurrentFormConfig();
        var (ok, msg) = await _orchestrator.TestSqlAsync(cfg);
        _lblSqlStatus.Text = $"SQL: {(ok ? "connected" : "failed")} — {msg}";
        _lblSqlStatus.ForeColor = ok ? Color.Green : Color.Red;
    }

    private async Task TestTallyClicked()
    {
        var cfg = CurrentFormConfig();
        var (ok, msg) = await _orchestrator.TestTallyAsync(cfg);
        _lblTallyStatus.Text = $"Tally: {(ok ? "reachable" : "unreachable")} — {msg}";
        _lblTallyStatus.ForeColor = ok ? Color.Green : Color.Red;
    }

    // Builds a config snapshot from whatever's currently typed in the
    // Configuration tab, without requiring Save first — so Test buttons
    // reflect what the user is about to save.
    private AgentConfig CurrentFormConfig() => new()
    {
        SqlServer = _txtSqlServer.Text.Trim(),
        SqlDatabase = _txtSqlDatabase.Text.Trim(),
        SqlUser = _txtSqlUser.Text.Trim(),
        SqlPasswordProtected = ConfigService.ProtectPassword(_txtSqlPassword.Text),
        SqlEncrypt = _chkEncrypt.Checked,
        TallyUrl = _txtTallyUrl.Text.Trim()
    };

    private void UpdateStatus(StatusEventArgs e)
    {
        if (InvokeRequired) { BeginInvoke(() => UpdateStatus(e)); return; }

        _lblRunState.Text = e.IsSyncing ? "Agent: syncing…" : (_orchestrator.IsRunning ? "Agent: running" : "Agent: stopped");
        _lblLastMaster.Text = "Last master sync: " + (e.LastMasterSyncAt?.ToString("dd MMM, HH:mm:ss") ?? "never");
        _lblNextMaster.Text = "Next master sync: " + (e.NextMasterSyncAt?.ToString("dd MMM, HH:mm:ss") ?? "—");
        _lblLastOrder.Text = "Last order cycle: " + (e.LastOrderCycleAt?.ToString("dd MMM, HH:mm:ss") ?? "never");
        _lblNextOrder.Text = "Next order cycle: " + (e.NextOrderCycleAt?.ToString("dd MMM, HH:mm:ss") ?? "—");
    }

    private void AppendLogLine(LogEventArgs e)
    {
        if (InvokeRequired) { BeginInvoke(() => AppendLogLine(e)); return; }

        var line = $"{e.Time:HH:mm:ss} [{e.Level}] {e.Message}{Environment.NewLine}";
        _txtLogs.AppendText(line);

        if (e.Level == LogLevel.Error)
        {
            _trayIcon.ShowBalloonTip(4000, "SaleOrd Sync Agent", e.Message, ToolTipIcon.Error);
        }
    }

    private static void SetAutoStart(bool enabled)
    {
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Run", true);
            if (key == null) return;
            const string valueName = "SaleOrdSyncAgent";
            if (enabled)
            {
                var exePath = Application.ExecutablePath;
                key.SetValue(valueName, $"\"{exePath}\"");
            }
            else
            {
                key.DeleteValue(valueName, false);
            }
        }
        catch { /* non-fatal — auto-start is a convenience, not required for the agent to work */ }
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            _orchestrator.Stop();
            _trayIcon.Visible = false;
            _trayIcon.Dispose();
        }
        base.Dispose(disposing);
    }
}
