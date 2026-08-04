using System.Reflection;
using Microsoft.Win32;
using SalesPush.SyncAgent.Licensing;
using SalesPush.SyncAgent.Models;
using SalesPush.SyncAgent.Services;

namespace SalesPush.SyncAgent.Forms;

public class FrmMain : Form
{
    private readonly ConfigService _configSvc = new();
    private readonly AgentLogger _logger;
    private readonly SyncOrchestrator _orchestrator;
    private readonly LicenseService _licenseSvc;
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

    // License section (Config tab)
    private Label _lblLicenseStatus = new();
    private Label _lblLicenseKeyMasked = new();

    // Database section (Config tab) — groundwork only, see AgentConfig.cs
    private TextBox _txtSqlConnectionString = new();

    // Logs tab
    private TextBox _txtLogs = new();

    private NotifyIcon _trayIcon = new();

    public FrmMain()
    {
        _logger = new AgentLogger(ConfigService.LogFilePath);
        _orchestrator = new SyncOrchestrator(_logger);
        _licenseSvc = new LicenseService(_configSvc);

        var version = Assembly.GetExecutingAssembly().GetName().Version?.ToString(3) ?? "1.0.0";
        Text = $"SalesPush Sync Agent — v{version}";
        Width = 900;
        Height = 700;
        StartPosition = FormStartPosition.CenterScreen;
        WindowState = FormWindowState.Maximized;
        MinimumSize = new Size(760, 560);
        BackColor = UiTheme.Background;
        Font = new Font("Segoe UI", 9.5f);

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
            _btnStartStop.Text = "⏹  Stop Agent";
            UiTheme.StyleButton(_btnStartStop, UiTheme.Danger);
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
        var tabs = new TabControl { Dock = DockStyle.Fill, Font = new Font("Segoe UI", 9.5f), Padding = new Point(16, 6) };

        var tabStatus = new TabPage("📊  Status") { BackColor = UiTheme.Background };
        var tabConfig = new TabPage("⚙️  Configuration") { BackColor = UiTheme.Background };
        var tabLogs = new TabPage("📝  Logs") { BackColor = UiTheme.Background };

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
        var card = UiTheme.CreateCard("⚡  Agent Control");
        card.Dock = DockStyle.Top;
        card.Height = 150;
        card.Margin = new Padding(16);

        _lblRunState.Text = "● Agent: stopped";
        _lblRunState.AutoSize = true;
        _lblRunState.Font = new Font("Segoe UI", 12f, FontStyle.Bold);
        _lblRunState.ForeColor = UiTheme.Danger;
        _lblRunState.Location = new Point(20, 50);

        _btnStartStop.Text = "▶  Start Agent";
        UiTheme.StyleButton(_btnStartStop, UiTheme.Success);
        _btnStartStop.Location = new Point(20, 90);
        _btnStartStop.Click += BtnStartStop_Click;

        _btnPushNow.Text = "⬆  Push Now";
        UiTheme.StyleButton(_btnPushNow, UiTheme.Primary);
        _btnPushNow.Location = new Point(_btnStartStop.Right + 12, 90);
        _btnPushNow.Click += async (_, _) => await PushNowClicked();

        card.Controls.Add(_lblRunState);
        card.Controls.Add(_btnStartStop);
        card.Controls.Add(_btnPushNow);

        var cardConn = UiTheme.CreateCard("🔌  Connections");
        cardConn.Dock = DockStyle.Top;
        cardConn.Height = 150;
        cardConn.Margin = new Padding(16, 0, 16, 16);

        var btnTestApi = new Button { Text = "Test API", Location = new Point(20, 54) };
        UiTheme.StyleButton(btnTestApi, UiTheme.Info);
        btnTestApi.Click += async (_, _) => await TestApiClicked();

        _lblApiStatus = UiTheme.StatusChip("API: unknown", UiTheme.TextMuted);
        _lblApiStatus.Location = new Point(btnTestApi.Right + 16, 62);

        var btnTestTally = new Button { Text = "Test Tally", Location = new Point(20, 96) };
        UiTheme.StyleButton(btnTestTally, UiTheme.Info);
        btnTestTally.Click += async (_, _) => await TestTallyClicked();

        _lblTallyStatus = UiTheme.StatusChip("Tally: unknown", UiTheme.TextMuted);
        _lblTallyStatus.Location = new Point(btnTestTally.Right + 16, 104);

        cardConn.Controls.Add(btnTestApi);
        cardConn.Controls.Add(_lblApiStatus);
        cardConn.Controls.Add(btnTestTally);
        cardConn.Controls.Add(_lblTallyStatus);

        var cardSchedule = UiTheme.CreateCard("🕐  Schedule");
        cardSchedule.Dock = DockStyle.Top;
        cardSchedule.Height = 130;
        cardSchedule.Margin = new Padding(16, 0, 16, 16);

        _lblLastPush.Text = "Last push cycle: never";
        _lblLastPush.AutoSize = true;
        _lblLastPush.Location = new Point(20, 54);

        _lblNextPush.Text = "Next push cycle: —";
        _lblNextPush.AutoSize = true;
        _lblNextPush.Location = new Point(20, 80);

        var lblLogPath = new Label
        {
            Text = "Log file: " + ConfigService.LogFilePath,
            AutoSize = true,
            ForeColor = UiTheme.TextMuted,
            Font = new Font("Segoe UI", 8f),
            Location = new Point(20, 106)
        };

        cardSchedule.Controls.Add(_lblLastPush);
        cardSchedule.Controls.Add(_lblNextPush);
        cardSchedule.Controls.Add(lblLogPath);

        var flow = new FlowLayoutPanel { Dock = DockStyle.Fill, FlowDirection = FlowDirection.TopDown, WrapContents = false, AutoScroll = true };
        flow.Controls.Add(card);
        flow.Controls.Add(cardConn);
        flow.Controls.Add(cardSchedule);
        card.Width = cardConn.Width = cardSchedule.Width = 620;

        page.Controls.Add(flow);
    }

    private void BuildConfigTab(TabPage page)
    {
        var panel = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 2,
            Padding = new Padding(20),
            AutoSize = true,
            AutoScroll = true,
            BackColor = UiTheme.Background
        };
        panel.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 200));
        panel.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));

        void AddRow(string label, Control control)
        {
            var r = panel.RowCount;
            panel.RowCount = r + 1;
            control.Dock = DockStyle.Fill;
            if (control is TextBox tb)
            {
                tb.BorderStyle = BorderStyle.FixedSingle;
                tb.BackColor = Color.White;
                tb.Margin = new Padding(0, 4, 0, 4);
            }
            panel.Controls.Add(new Label { Text = label, AutoSize = true, TextAlign = ContentAlignment.MiddleLeft, Anchor = AnchorStyles.Left, ForeColor = UiTheme.TextDark }, 0, r);
            panel.Controls.Add(control, 1, r);
        }

        void AddSectionHeader(string text, Color color, bool first = false)
        {
            var r = panel.RowCount;
            panel.RowCount = r + 1;
            var lbl = new Label
            {
                Text = text,
                AutoSize = true,
                Font = new Font("Segoe UI", 11f, FontStyle.Bold),
                ForeColor = color,
                Margin = new Padding(0, first ? 0 : 22, 0, 8)
            };
            panel.Controls.Add(lbl, 0, r);
            panel.SetColumnSpan(lbl, 2);
        }

        _txtApiBaseUrl.Width = 380;
        _txtApiKey.Width = 380;
        _txtApiKey.UseSystemPasswordChar = true;
        _txtTallyUrl.Width = 380;
        _txtSalesLedger.Width = 380;
        _txtIgstLedger.Width = 380;
        _txtCgstLedger.Width = 380;
        _txtSgstLedger.Width = 380;
        _txtRoundOffLedger.Width = 380;
        _txtVoucherType.Width = 380;
        _txtBatchName.Width = 380;
        _txtSqlConnectionString.Width = 380;
        _txtSqlConnectionString.UseSystemPasswordChar = true;
        _txtSqlConnectionString.PlaceholderText = "(not configured yet — groundwork for a later feature)";

        _numPushInterval.Minimum = 1;
        _numPushInterval.Maximum = 1440;
        _numPushInterval.Value = 3;
        _numPushInterval.Width = 100;

        AddSectionHeader("🌐  Sales API", UiTheme.Primary, first: true);
        AddRow("Sales API base URL", _txtApiBaseUrl);
        AddRow("API key", _txtApiKey);

        AddSectionHeader("📇  Tally Connection", UiTheme.Primary);
        AddRow("Local Tally URL", _txtTallyUrl);

        AddSectionHeader("🧾  Ledger Mapping", UiTheme.Primary);
        AddRow("Sales ledger", _txtSalesLedger);
        AddRow("IGST ledger", _txtIgstLedger);
        AddRow("CGST ledger", _txtCgstLedger);
        AddRow("SGST ledger", _txtSgstLedger);
        AddRow("Round off ledger", _txtRoundOffLedger);
        AddRow("Voucher type", _txtVoucherType);
        AddRow("Batch name", _txtBatchName);

        AddSectionHeader("🕐  Schedule && Startup", UiTheme.Primary);
        AddRow("Push interval (min)", _numPushInterval);
        AddRow("Start minimized to tray", _chkStartMinimized);
        AddRow("Start with Windows", _chkAutoStart);

        // ── License ──────────────────────────────────────────────────────
        AddSectionHeader("🔑  License", UiTheme.Warning);

        _lblLicenseKeyMasked.Text = "(none activated)";
        _lblLicenseKeyMasked.AutoSize = true;
        _lblLicenseKeyMasked.Font = new Font("Consolas", 9.5f);
        AddRow("Current key", _lblLicenseKeyMasked);

        _lblLicenseStatus = UiTheme.StatusChip("Unknown", UiTheme.TextMuted);
        AddRow("Status", _lblLicenseStatus);

        var btnChangeLicense = new Button { Text = "Change License Key", AutoSize = true };
        UiTheme.StyleButton(btnChangeLicense, UiTheme.Warning);
        btnChangeLicense.Click += (_, _) => ChangeLicenseClicked();
        AddRow("", btnChangeLicense);

        // ── Database (groundwork only — see AgentConfig.cs) ────────────────
        AddSectionHeader("🗄  Database (optional — for a future report-sync feature)", UiTheme.Info);
        AddRow("SQL connection string", _txtSqlConnectionString);

        var btnSave = new Button { Text = "💾  Save && Apply", AutoSize = true };
        UiTheme.StyleButton(btnSave, UiTheme.Success);
        btnSave.Click += BtnSaveConfig_Click;

        _lblConfigMsg.AutoSize = true;
        _lblConfigMsg.ForeColor = UiTheme.Success;
        _lblConfigMsg.Font = new Font("Segoe UI", 9.5f, FontStyle.Bold);

        var r2 = panel.RowCount;
        panel.RowCount = r2 + 1;
        panel.Controls.Add(btnSave, 1, r2);
        panel.Controls[panel.Controls.Count - 1].Margin = new Padding(0, 20, 0, 6);
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
        _txtLogs.BackColor = Color.FromArgb(30, 30, 30);
        _txtLogs.ForeColor = Color.Gainsboro;
        _txtLogs.BorderStyle = BorderStyle.None;

        var btnClear = new Button { Text = "🗑  Clear", Dock = DockStyle.Top, AutoSize = true, Margin = new Padding(0, 0, 0, 8) };
        UiTheme.StyleButton(btnClear, UiTheme.TextMuted);
        btnClear.Click += (_, _) => _txtLogs.Clear();

        var wrap = new Panel { Dock = DockStyle.Fill, Padding = new Padding(16), BackColor = UiTheme.Background };
        wrap.Controls.Add(_txtLogs);
        wrap.Controls.Add(btnClear);

        page.Controls.Add(wrap);
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
        WindowState = FormWindowState.Maximized;
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
        _txtSqlConnectionString.Text = ConfigService.Unprotect(_config.SqlConnectionStringProtected);

        RefreshLicenseDisplay();
    }

    private void RefreshLicenseDisplay()
    {
        var key = LicenseService.GetLicenseKey(_config);
        _lblLicenseKeyMasked.Text = string.IsNullOrEmpty(key) ? "(none activated)" : LicenseKeyMask.Mask(key);

        _ = RefreshLicenseStatusAsync();
    }

    private async Task RefreshLicenseStatusAsync()
    {
        var result = await _licenseSvc.CheckAsync(_config);
        if (InvokeRequired) { BeginInvoke(() => ApplyLicenseStatus(result)); return; }
        ApplyLicenseStatus(result);
    }

    private void ApplyLicenseStatus(LicenseValidationResult result)
    {
        var color = result.IsValid ? UiTheme.Success : UiTheme.Danger;
        var text = result.IsValid
            ? (result.IsTrial ? "Trial" : "Licensed") + (result.ExpiresOn.HasValue ? $" — expires {result.ExpiresOn.Value:dd MMM yyyy}" : "")
            : result.Message;
        _lblLicenseStatus.ForeColor = color;
        _lblLicenseStatus.Text = "● " + text;
    }

    private void ChangeLicenseClicked()
    {
        using var frm = new FrmLicense(_licenseSvc, _config);
        frm.ShowDialog(this);
        RefreshLicenseDisplay();
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
        _config.SqlConnectionStringProtected = ConfigService.Protect(_txtSqlConnectionString.Text.Trim());

        _configSvc.Save(_config);
        SetAutoStart(_config.AutoStartWithWindows);

        _orchestrator.Stop();
        _orchestrator.Start(_config);
        _btnStartStop.Text = "⏹  Stop Agent";
        UiTheme.StyleButton(_btnStartStop, UiTheme.Danger);

        _lblConfigMsg.ForeColor = UiTheme.Success;
        _lblConfigMsg.Text = $"✓ Saved and applied at {DateTime.Now:HH:mm:ss}.";
        _logger.Info("Configuration saved and agent restarted with new settings.");
    }

    private void BtnStartStop_Click(object? sender, EventArgs e)
    {
        if (_orchestrator.IsRunning)
        {
            _orchestrator.Stop();
            _btnStartStop.Text = "▶  Start Agent";
            UiTheme.StyleButton(_btnStartStop, UiTheme.Success);
        }
        else
        {
            _orchestrator.Start(_config);
            _btnStartStop.Text = "⏹  Stop Agent";
            UiTheme.StyleButton(_btnStartStop, UiTheme.Danger);
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
        _lblApiStatus.Text = $"● API: {(ok ? "connected" : "failed")} — {msg}";
        _lblApiStatus.ForeColor = ok ? UiTheme.Success : UiTheme.Danger;
    }

    private async Task TestTallyClicked()
    {
        var cfg = CurrentFormConfig();
        var (ok, msg) = await _orchestrator.TestTallyAsync(cfg);
        _lblTallyStatus.Text = $"● Tally: {(ok ? "reachable" : "unreachable")} — {msg}";
        _lblTallyStatus.ForeColor = ok ? UiTheme.Success : UiTheme.Danger;
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

        var running = _orchestrator.IsRunning;
        _lblRunState.Text = "● " + (e.IsSyncing ? "Agent: syncing…" : (running ? "Agent: running" : "Agent: stopped"));
        _lblRunState.ForeColor = e.IsSyncing ? UiTheme.Warning : (running ? UiTheme.Success : UiTheme.Danger);
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
