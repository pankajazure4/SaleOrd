using System.Reflection;
using Microsoft.Win32;
using SalesPush.SyncAgent.Models;
using SalesPush.SyncAgent.Services;

namespace SalesPush.SyncAgent.Forms;

public class FrmMain : Form
{
    private readonly ConfigService _configSvc = new();
    private readonly AgentLogger _logger;
    private readonly SyncOrchestrator _orchestrator;
    private AgentConfig _config = new();
    private bool _reallyExit;

    // Status tab controls
    private Label _lblApiStatus = new();
    private Label _lblTallyStatus = new();
    private Label _lblLastPush = new();
    private Label _lblNextPush = new();
    private Label _lblRunState = new();
    private Button _btnStartStop = new();
    private Button _btnPushNow = new();

    // Config tab controls
    private TextBox _txtApiBaseUrl = new();
    private TextBox _txtApiKey = new();
    private TextBox _txtTallyUrl = new();
    private TextBox _txtSalesLedger = new();
    private TextBox _txtIgstLedger = new();
    private TextBox _txtCgstLedger = new();
    private TextBox _txtSgstLedger = new();
    private TextBox _txtRoundOffLedger = new();
    private TextBox _txtVoucherType = new();
    private TextBox _txtBatchName = new();
    private NumericUpDown _numPushInterval = new();
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
        Text = $"SalesPush Sync Agent — v{version}";
        Width = 720;
        Height = 620;
        StartPosition = FormStartPosition.CenterScreen;
        MinimumSize = new Size(640, 520);

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

        if (!string.IsNullOrWhiteSpace(_config.ApiBaseUrl) && !string.IsNullOrWhiteSpace(_config.TallyUrl))
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

        var btnTestApi = new Button { Text = "Test API", AutoSize = true };
        btnTestApi.Click += async (_, _) => await TestApiClicked();

        var btnTestTally = new Button { Text = "Test Tally", AutoSize = true };
        btnTestTally.Click += async (_, _) => await TestTallyClicked();

        _lblApiStatus.Text = "API: unknown";
        _lblApiStatus.AutoSize = true;
        _lblTallyStatus.Text = "Tally: unknown";
        _lblTallyStatus.AutoSize = true;

        _lblLastPush.Text = "Last push cycle: never";
        _lblLastPush.AutoSize = true;
        _lblNextPush.Text = "Next push cycle: —";
        _lblNextPush.AutoSize = true;

        _lblRunState.Text = "Agent: stopped";
        _lblRunState.AutoSize = true;
        _lblRunState.Font = new Font(_lblRunState.Font, FontStyle.Bold);

        _btnStartStop.Text = "Start Agent";
        _btnStartStop.AutoSize = true;
        _btnStartStop.Click += BtnStartStop_Click;

        _btnPushNow.Text = "Push Now";
        _btnPushNow.AutoSize = true;
        _btnPushNow.Click += async (_, _) => await PushNowClicked();

        var lblLogPath = new Label
        {
            Text = "Log file: " + ConfigService.LogFilePath,
            AutoSize = true,
            ForeColor = Color.Gray,
            Font = new Font(_lblRunState.Font.FontFamily, 8)
        };

        int row = 0;
        panel.RowCount = 8;
        panel.Controls.Add(_lblRunState, 0, row); panel.Controls.Add(_btnStartStop, 1, row); panel.Controls.Add(_btnPushNow, 2, row); row++;
        panel.Controls.Add(new Label { Text = "", Height = 10 }, 0, row); row++;
        panel.Controls.Add(_lblApiStatus, 0, row); panel.Controls.Add(btnTestApi, 1, row); row++;
        panel.Controls.Add(_lblTallyStatus, 0, row); panel.Controls.Add(btnTestTally, 1, row); row++;
        panel.Controls.Add(new Label { Text = "", Height = 10 }, 0, row); row++;
        panel.Controls.Add(_lblLastPush, 0, row); panel.Controls.Add(_lblNextPush, 1, row); row++;
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
            AutoSize = true,
            AutoScroll = true
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

        _txtApiBaseUrl.Width = 320;
        _txtApiKey.Width = 320;
        _txtApiKey.UseSystemPasswordChar = true;
        _txtTallyUrl.Width = 320;
        _txtSalesLedger.Width = 320;
        _txtIgstLedger.Width = 320;
        _txtCgstLedger.Width = 320;
        _txtSgstLedger.Width = 320;
        _txtRoundOffLedger.Width = 320;
        _txtVoucherType.Width = 320;
        _txtBatchName.Width = 320;

        _numPushInterval.Minimum = 1;
        _numPushInterval.Maximum = 1440;
        _numPushInterval.Value = 3;

        AddRow("Sales API base URL", _txtApiBaseUrl);
        AddRow("API key", _txtApiKey);
        AddRow("Local Tally URL", _txtTallyUrl);
        AddRow("Sales ledger", _txtSalesLedger);
        AddRow("IGST ledger", _txtIgstLedger);
        AddRow("CGST ledger", _txtCgstLedger);
        AddRow("SGST ledger", _txtSgstLedger);
        AddRow("Round off ledger", _txtRoundOffLedger);
        AddRow("Voucher type", _txtVoucherType);
        AddRow("Batch name", _txtBatchName);
        AddRow("Push interval (min)", _numPushInterval);
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
        _trayIcon.Text = "SalesPush Sync Agent";
        _trayIcon.Visible = true;

        var menu = new ContextMenuStrip();
        menu.Items.Add("Open", null, (_, _) => RestoreFromTray());
        menu.Items.Add("Push Now", null, async (_, _) => await PushNowClicked());
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
        // background agent, accidental close shouldn't stop the push cycle.
        e.Cancel = true;
        WindowState = FormWindowState.Minimized;
        Hide();
    }

    // ── Actions ──────────────────────────────────────────────────────────

    private void PopulateConfigFields()
    {
        _txtApiBaseUrl.Text = _config.ApiBaseUrl;
        _txtApiKey.Text = ConfigService.Unprotect(_config.ApiKeyProtected);
        _txtTallyUrl.Text = _config.TallyUrl;
        _txtSalesLedger.Text = _config.SalesLedger;
        _txtIgstLedger.Text = _config.IGSTLedger;
        _txtCgstLedger.Text = _config.CGSTLedger;
        _txtSgstLedger.Text = _config.SGSTLedger;
        _txtRoundOffLedger.Text = _config.RoundOffLedger;
        _txtVoucherType.Text = _config.VoucherType;
        _txtBatchName.Text = _config.BatchName;
        _numPushInterval.Value = Math.Clamp(_config.PushIntervalMinutes, 1, 1440);
        _chkStartMinimized.Checked = _config.StartMinimized;
        _chkAutoStart.Checked = _config.AutoStartWithWindows;
    }

    private void BtnSaveConfig_Click(object? sender, EventArgs e)
    {
        _config.ApiBaseUrl = _txtApiBaseUrl.Text.Trim();
        _config.ApiKeyProtected = ConfigService.Protect(_txtApiKey.Text);
        _config.TallyUrl = _txtTallyUrl.Text.Trim();
        _config.SalesLedger = _txtSalesLedger.Text.Trim();
        _config.IGSTLedger = _txtIgstLedger.Text.Trim();
        _config.CGSTLedger = _txtCgstLedger.Text.Trim();
        _config.SGSTLedger = _txtSgstLedger.Text.Trim();
        _config.RoundOffLedger = _txtRoundOffLedger.Text.Trim();
        _config.VoucherType = _txtVoucherType.Text.Trim();
        _config.BatchName = _txtBatchName.Text.Trim();
        _config.PushIntervalMinutes = (int)_numPushInterval.Value;
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

    private async Task PushNowClicked()
    {
        _btnPushNow.Enabled = false;
        try { await _orchestrator.RunNowAsync(); }
        finally { _btnPushNow.Enabled = true; }
    }

    private async Task TestApiClicked()
    {
        var cfg = CurrentFormConfig();
        var (ok, msg) = await _orchestrator.TestApiAsync(cfg);
        _lblApiStatus.Text = $"API: {(ok ? "connected" : "failed")} — {msg}";
        _lblApiStatus.ForeColor = ok ? Color.Green : Color.Red;
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
        ApiBaseUrl = _txtApiBaseUrl.Text.Trim(),
        ApiKeyProtected = ConfigService.Protect(_txtApiKey.Text),
        TallyUrl = _txtTallyUrl.Text.Trim()
    };

    private void UpdateStatus(StatusEventArgs e)
    {
        if (InvokeRequired) { BeginInvoke(() => UpdateStatus(e)); return; }

        _lblRunState.Text = e.IsSyncing ? "Agent: syncing…" : (_orchestrator.IsRunning ? "Agent: running" : "Agent: stopped");
        _lblLastPush.Text = "Last push cycle: " + (e.LastPushCycleAt?.ToString("dd MMM, HH:mm:ss") ?? "never");
        _lblNextPush.Text = "Next push cycle: " + (e.NextPushCycleAt?.ToString("dd MMM, HH:mm:ss") ?? "—");
    }

    private void AppendLogLine(LogEventArgs e)
    {
        if (InvokeRequired) { BeginInvoke(() => AppendLogLine(e)); return; }

        var line = $"{e.Time:HH:mm:ss} [{e.Level}] {e.Message}{Environment.NewLine}";
        _txtLogs.AppendText(line);

        if (e.Level == LogLevel.Error)
        {
            _trayIcon.ShowBalloonTip(4000, "SalesPush Sync Agent", e.Message, ToolTipIcon.Error);
        }
    }

    private static void SetAutoStart(bool enabled)
    {
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Run", true);
            if (key == null) return;
            const string valueName = "SalesPushSyncAgent";
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
