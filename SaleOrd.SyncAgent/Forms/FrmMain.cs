using System.Reflection;
using Microsoft.Win32;
using SaleOrd.SyncAgent.Licensing;
using SaleOrd.SyncAgent.Models;
using SaleOrd.SyncAgent.Services;

namespace SaleOrd.SyncAgent.Forms;

public class FrmMain : Form
{
    private readonly ConfigService _configSvc = new();
    private readonly AgentLogger _logger;
    private readonly SyncOrchestrator _orchestrator;
    private readonly LicenseService _licenseSvc;
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
    private Button _btnSyncRates = new();

    // Config tab controls
    private TextBox _txtSqlServer = new();
    private TextBox _txtSqlDatabase = new();
    private TextBox _txtSqlUser = new();
    private TextBox _txtSqlPassword = new();
    private CheckBox _chkEncrypt = new();
    private TextBox _txtTallyUrl = new();
    private NumericUpDown _numMasterInterval = new();
    private NumericUpDown _numOrderInterval = new();
    private NumericUpDown _numRatesInterval = new();
    private CheckBox _chkStartMinimized = new();
    private CheckBox _chkAutoStart = new();
    private Label _lblConfigMsg = new();

    // License section (Config tab)
    private Label _lblLicenseStatus = new();
    private Label _lblLicenseKeyMasked = new();

    // Logs tab
    private TextBox _txtLogs = new();

    private NotifyIcon _trayIcon = new();

    private const int FieldWidth = 300;

    public FrmMain()
    {
        _logger = new AgentLogger(ConfigService.LogFilePath);
        _orchestrator = new SyncOrchestrator(_logger);
        _licenseSvc = new LicenseService(_configSvc);

        var version = Assembly.GetExecutingAssembly().GetName().Version?.ToString(3) ?? "1.0.0";
        Text = $"SaleOrd Sync Agent — v{version}";
        Width = 1000;
        Height = 720;
        StartPosition = FormStartPosition.CenterScreen;
        WindowState = FormWindowState.Maximized;
        MinimumSize = new Size(820, 600);
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

        if (!string.IsNullOrWhiteSpace(_config.SqlServer) && !string.IsNullOrWhiteSpace(_config.TallyUrl))
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
    //
    // Ported from SalesPush.SyncAgent's redesigned FrmMain: native GroupBox
    // containers (Dock=Top + AutoSize=true — NOT Dock=Fill, which measures
    // circularly and clips) arranged in a 2-column TableLayoutPanel grid.
    // All the existing SQL/Tally/interval fields and their behaviors below
    // are unchanged from the original — only how they're laid out and
    // styled changed.

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

        // A bottom bar outside the TabControl — not tied to any one tab, so
        // Exit stays reachable no matter which tab is open, without hunting
        // for the tray icon.
        var bottomBar = new Panel { Dock = DockStyle.Bottom, Height = 52, BackColor = UiTheme.Card, Padding = new Padding(16, 8, 16, 8) };
        bottomBar.Paint += (_, e) => e.Graphics.DrawLine(new Pen(UiTheme.Border), 0, 0, bottomBar.Width, 0);

        var btnExit = new Button { Text = "✕  Exit", AutoSize = true, Anchor = AnchorStyles.Right | AnchorStyles.Top };
        UiTheme.StyleButton(btnExit, UiTheme.Danger);
        btnExit.Location = new Point(bottomBar.Width - btnExit.PreferredSize.Width - 16, 8);
        btnExit.Click += (_, _) => { _reallyExit = true; Close(); };
        bottomBar.Controls.Add(btnExit);
        bottomBar.Resize += (_, _) => btnExit.Location = new Point(bottomBar.Width - btnExit.Width - 16, 8);

        var lblHint = new Label
        {
            Text = "Closing the window minimizes to tray and keeps syncing — use Exit to fully quit.",
            AutoSize = true,
            ForeColor = UiTheme.TextMuted,
            Font = new Font("Segoe UI", 8.5f),
            Location = new Point(0, 16)
        };
        bottomBar.Controls.Add(lblHint);

        // Dock order matters: Dock=Bottom must be added before Dock=Fill, or
        // the Fill control (tabs) claims the whole client area first and
        // leaves nothing for the bottom bar to dock into.
        Controls.Add(bottomBar);
        Controls.Add(tabs);
    }

    private static GroupBox MakeGroupBox(string title, Control content)
    {
        var gb = new GroupBox
        {
            Text = title,
            Dock = DockStyle.Top,
            AutoSize = true,
            AutoSizeMode = AutoSizeMode.GrowAndShrink,
            Font = new Font("Segoe UI", 10f, FontStyle.Bold),
            ForeColor = UiTheme.Primary,
            Padding = new Padding(12, 8, 12, 12),
            Margin = new Padding(10)
        };
        content.Dock = DockStyle.Top;
        content.Font = new Font("Segoe UI", 9.5f, FontStyle.Regular);
        gb.Controls.Add(content);
        return gb;
    }

    private static TableLayoutPanel MakeFieldGrid()
    {
        var p = new TableLayoutPanel { Dock = DockStyle.Top, ColumnCount = 2, AutoSize = true, GrowStyle = TableLayoutPanelGrowStyle.AddRows };
        p.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 150));
        p.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        return p;
    }

    // Field controls get a fixed pixel width and stay left-anchored rather
    // than Dock=Fill — otherwise a textbox stretches to whatever its
    // container happens to be.
    private static void AddField(TableLayoutPanel p, string label, Control control)
    {
        var r = p.RowCount;
        p.RowCount = r + 1;
        control.Anchor = AnchorStyles.Left;
        if (control is TextBox tb)
        {
            tb.Width = FieldWidth;
            tb.BorderStyle = BorderStyle.FixedSingle;
            tb.Margin = new Padding(0, 4, 0, 4);
        }
        p.Controls.Add(new Label { Text = label, AutoSize = true, TextAlign = ContentAlignment.MiddleLeft, Anchor = AnchorStyles.Left, ForeColor = UiTheme.TextDark }, 0, r);
        p.Controls.Add(control, 1, r);
    }

    private void BuildStatusTab(TabPage page)
    {
        var outer = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 2,
            AutoScroll = true,
            Padding = new Padding(10),
            BackColor = UiTheme.Background
        };
        outer.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50));
        outer.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50));
        outer.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        outer.RowStyles.Add(new RowStyle(SizeType.AutoSize));

        // Agent Control
        var controlGrid = new TableLayoutPanel { Dock = DockStyle.Top, ColumnCount = 1, AutoSize = true };
        _lblRunState.Text = "● Agent: stopped";
        _lblRunState.AutoSize = true;
        _lblRunState.Font = new Font("Segoe UI", 12f, FontStyle.Bold);
        _lblRunState.ForeColor = UiTheme.Danger;
        _lblRunState.Margin = new Padding(0, 4, 0, 12);

        var btnRow = new FlowLayoutPanel { AutoSize = true, FlowDirection = FlowDirection.LeftToRight, WrapContents = false };
        _btnStartStop.Text = "▶  Start Agent";
        UiTheme.StyleButton(_btnStartStop, UiTheme.Success);
        _btnStartStop.Click += BtnStartStop_Click;
        _btnSyncNow.Text = "⟳  Sync Now";
        UiTheme.StyleButton(_btnSyncNow, UiTheme.Primary);
        _btnSyncNow.Margin = new Padding(10, 0, 0, 0);
        _btnSyncNow.Click += async (_, _) => await SyncNowClicked();
        // Separate from Sync Now on purpose — item rate history
        // (VoucherInventoryEntries, used for the last-sale-rate feature) can
        // take minutes on a full historical backfill, and used to run inline
        // inside every master sync, regularly starving order push out of its
        // own cycle since they share one lock. Run it here, on its own time,
        // whenever's convenient — not tied to the master/order schedule.
        _btnSyncRates.Text = "💰  Sync Rates";
        UiTheme.StyleButton(_btnSyncRates, UiTheme.Info);
        _btnSyncRates.Margin = new Padding(10, 0, 0, 0);
        _btnSyncRates.Click += async (_, _) => await SyncRatesClicked();
        btnRow.Controls.Add(_btnStartStop);
        btnRow.Controls.Add(_btnSyncNow);
        btnRow.Controls.Add(_btnSyncRates);

        controlGrid.Controls.Add(_lblRunState, 0, 0);
        controlGrid.Controls.Add(btnRow, 0, 1);

        // Connections — dedicated [button | message] grid so the button and
        // its status text sit side by side on the same row, not stacked or
        // sharing a cell.
        var connGrid = new TableLayoutPanel { Dock = DockStyle.Top, ColumnCount = 2, AutoSize = true, GrowStyle = TableLayoutPanelGrowStyle.AddRows };
        connGrid.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 130));
        connGrid.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));

        var btnTestSql = new Button { Text = "Test SQL", AutoSize = true, Anchor = AnchorStyles.Left, Margin = new Padding(0, 6, 0, 6) };
        UiTheme.StyleButton(btnTestSql, UiTheme.Info);
        btnTestSql.Click += async (_, _) => await TestSqlClicked();
        _lblSqlStatus = UiTheme.StatusChip("SQL: unknown", UiTheme.TextMuted);
        _lblSqlStatus.Anchor = AnchorStyles.Left;
        _lblSqlStatus.Margin = new Padding(12, 12, 0, 6);
        connGrid.Controls.Add(btnTestSql, 0, 0);
        connGrid.Controls.Add(_lblSqlStatus, 1, 0);

        var btnTestTally = new Button { Text = "Test Tally", AutoSize = true, Anchor = AnchorStyles.Left, Margin = new Padding(0, 6, 0, 6) };
        UiTheme.StyleButton(btnTestTally, UiTheme.Info);
        btnTestTally.Click += async (_, _) => await TestTallyClicked();
        _lblTallyStatus = UiTheme.StatusChip("Tally: unknown", UiTheme.TextMuted);
        _lblTallyStatus.Anchor = AnchorStyles.Left;
        _lblTallyStatus.Margin = new Padding(12, 12, 0, 6);
        connGrid.Controls.Add(btnTestTally, 0, 1);
        connGrid.Controls.Add(_lblTallyStatus, 1, 1);

        // Schedule — 2x2: Last/Next Master sync, Last/Next Order cycle
        var schedGrid = new TableLayoutPanel { Dock = DockStyle.Top, ColumnCount = 2, AutoSize = true };
        _lblLastMaster.Text = "Last master sync: never";
        _lblLastMaster.AutoSize = true;
        _lblLastMaster.Margin = new Padding(0, 2, 24, 6);
        _lblNextMaster.Text = "Next master sync: —";
        _lblNextMaster.AutoSize = true;
        _lblNextMaster.Margin = new Padding(0, 2, 0, 6);
        _lblLastOrder.Text = "Last order cycle: never";
        _lblLastOrder.AutoSize = true;
        _lblLastOrder.Margin = new Padding(0, 0, 24, 12);
        _lblNextOrder.Text = "Next order cycle: —";
        _lblNextOrder.AutoSize = true;
        _lblNextOrder.Margin = new Padding(0, 0, 0, 12);
        var lblLogPath = new Label
        {
            Text = "Log file: " + ConfigService.LogFilePath,
            AutoSize = true,
            ForeColor = UiTheme.TextMuted,
            Font = new Font("Segoe UI", 8f)
        };
        schedGrid.Controls.Add(_lblLastMaster, 0, 0);
        schedGrid.Controls.Add(_lblNextMaster, 1, 0);
        schedGrid.Controls.Add(_lblLastOrder, 0, 1);
        schedGrid.Controls.Add(_lblNextOrder, 1, 1);
        schedGrid.Controls.Add(lblLogPath, 0, 2);
        schedGrid.SetColumnSpan(lblLogPath, 2);

        var gbControl = MakeGroupBox("⚡  Agent Control", controlGrid);
        var gbConn = MakeGroupBox("🔌  Connections", connGrid);
        var gbSched = MakeGroupBox("🕐  Schedule", schedGrid);

        outer.Controls.Add(gbControl, 0, 0);
        outer.Controls.Add(gbConn, 1, 0);
        outer.Controls.Add(gbSched, 0, 1);
        outer.SetColumnSpan(gbSched, 2);

        page.Controls.Add(outer);
    }

    private void BuildConfigTab(TabPage page)
    {
        var outer = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 2,
            AutoScroll = true,
            Padding = new Padding(10),
            BackColor = UiTheme.Background
        };
        outer.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50));
        outer.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50));
        for (int i = 0; i < 3; i++) outer.RowStyles.Add(new RowStyle(SizeType.AutoSize));

        // SQL Server Connection
        var sqlGrid = MakeFieldGrid();
        AddField(sqlGrid, "Server", _txtSqlServer);
        AddField(sqlGrid, "Database", _txtSqlDatabase);
        AddField(sqlGrid, "User", _txtSqlUser);
        _txtSqlPassword.UseSystemPasswordChar = true;
        AddField(sqlGrid, "Password", _txtSqlPassword);
        AddField(sqlGrid, "Encrypt connection", _chkEncrypt);

        // Tally Connection
        var tallyGrid = MakeFieldGrid();
        AddField(tallyGrid, "Tally URL", _txtTallyUrl);

        // Schedule & Startup
        var schedCfgGrid = MakeFieldGrid();
        _numMasterInterval.Minimum = 1;
        _numMasterInterval.Maximum = 1440;
        _numMasterInterval.Value = 30;
        _numMasterInterval.Width = 100;
        AddField(schedCfgGrid, "Master sync (min)", _numMasterInterval);
        _numOrderInterval.Minimum = 1;
        _numOrderInterval.Maximum = 1440;
        _numOrderInterval.Value = 3;
        _numOrderInterval.Width = 100;
        AddField(schedCfgGrid, "Order push (min)", _numOrderInterval);
        _numRatesInterval.Minimum = 0;
        _numRatesInterval.Maximum = 1440;
        _numRatesInterval.Value = 0;
        _numRatesInterval.Width = 100;
        AddField(schedCfgGrid, "Sync Rates (min, 0=manual only)", _numRatesInterval);
        AddField(schedCfgGrid, "Start minimized", _chkStartMinimized);
        AddField(schedCfgGrid, "Start with Windows", _chkAutoStart);

        // License
        var licenseGrid = MakeFieldGrid();
        _lblLicenseKeyMasked.AutoSize = true;
        _lblLicenseKeyMasked.Font = new Font("Consolas", 9.5f);
        _lblLicenseKeyMasked.Text = "(none activated)";
        AddField(licenseGrid, "Current key", _lblLicenseKeyMasked);
        _lblLicenseStatus = UiTheme.StatusChip("Unknown", UiTheme.TextMuted);
        AddField(licenseGrid, "Status", _lblLicenseStatus);
        var btnReactivateLicense = new Button { Text = "🔄  Reactivate License", AutoSize = true };
        UiTheme.StyleButton(btnReactivateLicense, UiTheme.Warning);
        btnReactivateLicense.Click += (_, _) => ReactivateLicenseClicked();
        AddField(licenseGrid, "", btnReactivateLicense);

        var gbSql = MakeGroupBox("🗄  SQL Server Connection", sqlGrid);
        var gbTally = MakeGroupBox("📇  Tally Connection", tallyGrid);
        var gbSchedCfg = MakeGroupBox("🕐  Schedule && Startup", schedCfgGrid);
        var gbLicense = MakeGroupBox("🔑  License", licenseGrid);
        gbLicense.ForeColor = UiTheme.Warning;

        int row = 0;
        outer.Controls.Add(gbSql, 0, row);
        outer.Controls.Add(gbTally, 1, row); row++;
        outer.Controls.Add(gbSchedCfg, 0, row);
        outer.Controls.Add(gbLicense, 1, row); row++;

        var btnSave = new Button { Text = "💾  Save && Apply", AutoSize = true };
        UiTheme.StyleButton(btnSave, UiTheme.Success);
        btnSave.Click += BtnSaveConfig_Click;
        _lblConfigMsg.AutoSize = true;
        _lblConfigMsg.ForeColor = UiTheme.Success;
        _lblConfigMsg.Font = new Font("Segoe UI", 9.5f, FontStyle.Bold);
        _lblConfigMsg.Margin = new Padding(12, 8, 0, 0);

        var saveRow = new FlowLayoutPanel { AutoSize = true, FlowDirection = FlowDirection.LeftToRight, WrapContents = false, Margin = new Padding(10) };
        saveRow.Controls.Add(btnSave);
        saveRow.Controls.Add(_lblConfigMsg);
        outer.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        outer.Controls.Add(saveRow, 0, row);
        outer.SetColumnSpan(saveRow, 2);

        page.Controls.Add(outer);
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

        var btnClear = new Button { Text = "🗑  Clear", AutoSize = true, Margin = new Padding(0, 0, 0, 8) };
        UiTheme.StyleButton(btnClear, UiTheme.TextMuted);
        btnClear.Click += (_, _) => _txtLogs.Clear();

        var top = new Panel { Dock = DockStyle.Top, Height = 46, BackColor = UiTheme.Background };
        top.Controls.Add(btnClear);

        var logHolder = new Panel { Dock = DockStyle.Fill, Padding = new Padding(1), BackColor = UiTheme.Border };
        logHolder.Controls.Add(_txtLogs);

        var wrap = new Panel { Dock = DockStyle.Fill, Padding = new Padding(16), BackColor = UiTheme.Background };
        wrap.Controls.Add(logHolder);
        wrap.Controls.Add(top);

        page.Controls.Add(wrap);
    }

    private void BuildTrayIcon()
    {
        _trayIcon.Icon = Icon ?? SystemIcons.Application;
        _trayIcon.Text = "SaleOrd Sync Agent";
        _trayIcon.Visible = true;

        var menu = new ContextMenuStrip();
        menu.Items.Add("Open", null, (_, _) => RestoreFromTray());
        menu.Items.Add("Sync Now", null, async (_, _) => await SyncNowClicked());
        menu.Items.Add("Sync Rates", null, async (_, _) => await SyncRatesClicked());
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
        _numRatesInterval.Value = Math.Clamp(_config.VoucherRatesSyncIntervalMinutes, 0, 1440);
        _chkStartMinimized.Checked = _config.StartMinimized;
        _chkAutoStart.Checked = _config.AutoStartWithWindows;

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

    // A hard local reset — wipes the stored key and the offline-grace
    // snapshot, then restarts the whole process so it goes through the
    // normal Program.cs startup flow: license dialog only, FrmMain isn't
    // created at all until activation succeeds. Doesn't touch the portal
    // side (the key itself isn't deactivated there — MaxActivations still
    // applies), this just clears *this machine's* local state so it behaves
    // like a fresh, never-activated install.
    private void ReactivateLicenseClicked()
    {
        var confirm = MessageBox.Show(
            "This deactivates the license on this machine and restarts the agent at the activation screen. Continue?",
            "Reactivate License",
            MessageBoxButtons.YesNo,
            MessageBoxIcon.Warning);
        if (confirm != DialogResult.Yes) return;

        _config.LicenseKeyProtected = "";
        _config.LicenseLastValidatedOnUtc = "";
        _config.LicenseExpiresOnUtc = "";
        _config.LicenseIsTrial = false;
        _config.LicenseMachineId = "";
        _configSvc.Save(_config);

        _orchestrator.Stop();
        _reallyExit = true; // let this Close() actually exit instead of minimizing to tray
        Application.Restart();
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
        _config.VoucherRatesSyncIntervalMinutes = (int)_numRatesInterval.Value;
        _config.StartMinimized = _chkStartMinimized.Checked;
        _config.AutoStartWithWindows = _chkAutoStart.Checked;

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

    private async Task SyncNowClicked()
    {
        _btnSyncNow.Enabled = false;
        try { await _orchestrator.RunNowAsync(); }
        finally { _btnSyncNow.Enabled = true; }
    }

    private async Task SyncRatesClicked()
    {
        _btnSyncRates.Enabled = false;
        try { await _orchestrator.RunRatesSyncNowAsync(); }
        finally { _btnSyncRates.Enabled = true; }
    }

    private async Task TestSqlClicked()
    {
        var cfg = CurrentFormConfig();
        var (ok, msg) = await _orchestrator.TestSqlAsync(cfg);
        _lblSqlStatus.Text = $"● SQL: {(ok ? "connected" : "failed")} — {msg}";
        _lblSqlStatus.ForeColor = ok ? UiTheme.Success : UiTheme.Danger;
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

        var running = _orchestrator.IsRunning;
        _lblRunState.Text = "● " + (e.IsSyncing ? "Agent: syncing…" : (running ? "Agent: running" : "Agent: stopped"));
        _lblRunState.ForeColor = e.IsSyncing ? UiTheme.Warning : (running ? UiTheme.Success : UiTheme.Danger);
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
