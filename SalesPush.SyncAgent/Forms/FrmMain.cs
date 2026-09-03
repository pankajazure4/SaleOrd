using System.Reflection;
using Microsoft.Data.SqlClient;
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
    private TextBox _txtAuthHeaderName = new();
    private TextBox _txtAuthScheme = new();
    private TextBox _txtTallyUrl = new();
    private TextBox _txtVoucherType = new();
    private TextBox _txtBatchName = new();
    private TextBox _txtVoucherClass = new();
    private TextBox _txtTallyCompanyName = new();
    private CheckBox _chkPushAsOptional = new();
    private NumericUpDown _numPushInterval = new();
    private CheckBox _chkStartMinimized = new();
    private CheckBox _chkAutoStart = new();
    private Label _lblConfigMsg = new();

    // License section (Config tab)
    private Label _lblLicenseStatus = new();
    private Label _lblLicenseKeyMasked = new();

    // Database section (Config tab) — groundwork only, see AgentConfig.cs
    private TextBox _txtSqlServer = new();
    private TextBox _txtSqlDatabase = new();
    private TextBox _txtSqlUserId = new();
    private TextBox _txtSqlPassword = new();
    private Label _lblSqlStatus = new();
    private Button _btnTestSql = new();

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
        Text = $"SalesPush Sync Agent — v{version}";
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
    //
    // Everything here is built with native GroupBox containers (title +
    // border for free, no custom Paint/owner-draw needed) arranged in a
    // 2-column TableLayoutPanel grid. An earlier version used a
    // FlowLayoutPanel with Dock=Top children and absolutely-positioned
    // inner controls — that combination doesn't lay out reliably in
    // WinForms (the Status tab rendered blank), so every tab now follows
    // this one, reliable pattern: TableLayoutPanel grid of GroupBoxes, each
    // GroupBox containing a small label/control TableLayoutPanel of its own.

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
        // "Exit" (and anything else that belongs everywhere) stays reachable
        // no matter which tab is open, without hunting for the tray icon.
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
        // Dock=Top (not Fill) + AutoSize=true is deliberate: a TableLayoutPanel
        // row set to AutoSize measures a Dock=Fill child as "however big the
        // cell already is" (circular — it never grows the row), which is why
        // every GroupBox was clipping its own content. Dock=Top instead lets
        // the GroupBox report its true preferred height (stretching only to
        // the cell's width), so the AutoSize row actually grows to fit it.
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
        var p = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 2, AutoSize = true, GrowStyle = TableLayoutPanelGrowStyle.AddRows };
        p.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 130));
        p.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        return p;
    }

    // Field controls get a fixed pixel width and stay left-anchored rather
    // than Dock=Fill — otherwise a textbox stretches to whatever its
    // container happens to be (which looked huge when every field spanned
    // the full window width).
    private static void AddField(TableLayoutPanel p, string label, Control control, ContentAlignment align = ContentAlignment.MiddleLeft)
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
        p.Controls.Add(new Label { Text = label, AutoSize = true, TextAlign = align, Anchor = AnchorStyles.Left, ForeColor = UiTheme.TextDark }, 0, r);
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
        var controlGrid = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, AutoSize = true };
        _lblRunState.Text = "● Agent: stopped";
        _lblRunState.AutoSize = true;
        _lblRunState.Font = new Font("Segoe UI", 12f, FontStyle.Bold);
        _lblRunState.ForeColor = UiTheme.Danger;
        _lblRunState.Margin = new Padding(0, 4, 0, 12);

        var btnRow = new FlowLayoutPanel { AutoSize = true, FlowDirection = FlowDirection.LeftToRight, WrapContents = false };
        _btnStartStop.Text = "▶  Start Agent";
        UiTheme.StyleButton(_btnStartStop, UiTheme.Success);
        _btnStartStop.Click += BtnStartStop_Click;
        _btnPushNow.Text = "⬆  Push Now";
        UiTheme.StyleButton(_btnPushNow, UiTheme.Primary);
        _btnPushNow.Margin = new Padding(10, 0, 0, 0);
        _btnPushNow.Click += async (_, _) => await PushNowClicked();
        btnRow.Controls.Add(_btnStartStop);
        btnRow.Controls.Add(_btnPushNow);

        controlGrid.Controls.Add(_lblRunState, 0, 0);
        controlGrid.Controls.Add(btnRow, 0, 1);

        // Connections — a dedicated [button | message] grid, not the shared
        // label/value MakeFieldGrid (which was causing this: AddField put
        // the button in column 1, then the status label got added to that
        // *same* column-1 cell on top of it — two controls sharing one
        // TableLayoutPanel cell overlap instead of laying out side by side).
        var connGrid = new TableLayoutPanel { Dock = DockStyle.Top, ColumnCount = 2, AutoSize = true, GrowStyle = TableLayoutPanelGrowStyle.AddRows };
        connGrid.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 130));
        connGrid.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));

        var btnTestApi = new Button { Text = "Test API", AutoSize = true, Anchor = AnchorStyles.Left, Margin = new Padding(0, 6, 0, 6) };
        UiTheme.StyleButton(btnTestApi, UiTheme.Info);
        btnTestApi.Click += async (_, _) => await TestApiClicked();
        _lblApiStatus = UiTheme.StatusChip("API: unknown", UiTheme.TextMuted);
        _lblApiStatus.Anchor = AnchorStyles.Left;
        _lblApiStatus.Margin = new Padding(12, 12, 0, 6);
        connGrid.Controls.Add(btnTestApi, 0, 0);
        connGrid.Controls.Add(_lblApiStatus, 1, 0);

        var btnTestTally = new Button { Text = "Test Tally", AutoSize = true, Anchor = AnchorStyles.Left, Margin = new Padding(0, 6, 0, 6) };
        UiTheme.StyleButton(btnTestTally, UiTheme.Info);
        btnTestTally.Click += async (_, _) => await TestTallyClicked();
        _lblTallyStatus = UiTheme.StatusChip("Tally: unknown", UiTheme.TextMuted);
        _lblTallyStatus.Anchor = AnchorStyles.Left;
        _lblTallyStatus.Margin = new Padding(12, 12, 0, 6);
        connGrid.Controls.Add(btnTestTally, 0, 1);
        connGrid.Controls.Add(_lblTallyStatus, 1, 1);

        // Schedule
        var schedGrid = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, AutoSize = true };
        _lblLastPush.Text = "Last push cycle: never";
        _lblLastPush.AutoSize = true;
        _lblLastPush.Margin = new Padding(0, 2, 0, 6);
        _lblNextPush.Text = "Next push cycle: —";
        _lblNextPush.AutoSize = true;
        _lblNextPush.Margin = new Padding(0, 0, 0, 12);
        var lblLogPath = new Label
        {
            Text = "Log file: " + ConfigService.LogFilePath,
            AutoSize = true,
            ForeColor = UiTheme.TextMuted,
            Font = new Font("Segoe UI", 8f)
        };
        schedGrid.Controls.Add(_lblLastPush, 0, 0);
        schedGrid.Controls.Add(_lblNextPush, 0, 1);
        schedGrid.Controls.Add(lblLogPath, 0, 2);

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
        for (int i = 0; i < 5; i++) outer.RowStyles.Add(new RowStyle(SizeType.AutoSize));

        // Sales API
        var apiGrid = MakeFieldGrid();
        AddField(apiGrid, "Base URL", _txtApiBaseUrl);
        _txtApiKey.UseSystemPasswordChar = true;
        AddField(apiGrid, "API key", _txtApiKey);
        _txtAuthHeaderName.Text = "Authorization";
        AddField(apiGrid, "Auth header", _txtAuthHeaderName);
        _txtAuthScheme.Text = "Bearer";
        AddField(apiGrid, "Auth scheme", _txtAuthScheme);
        var lblAuthHint = new Label
        {
            Text = "E.g. \"Authorization\" + \"Bearer\" (default), or \"X-Api-Key\" with scheme left blank.",
            AutoSize = true,
            ForeColor = UiTheme.TextMuted,
            Font = new Font("Segoe UI", 8f)
        };
        var rAuth = apiGrid.RowCount;
        apiGrid.RowCount = rAuth + 1;
        apiGrid.Controls.Add(lblAuthHint, 1, rAuth);

        // Tally Connection
        var tallyGrid = MakeFieldGrid();
        AddField(tallyGrid, "Tally URL", _txtTallyUrl);

        // Fallback defaults (its own 4-column sub-grid: label,field,label,field)
        // — the real values normally come from the API per invoice/item; these
        // only fill in when a client's API leaves one out.
        var fallbackGrid = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 4, AutoSize = true };
        fallbackGrid.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 130));
        fallbackGrid.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50));
        fallbackGrid.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 130));
        fallbackGrid.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50));
        void AddFallbackPair(string l1, Control c1, string l2, Control c2)
        {
            var r = fallbackGrid.RowCount;
            fallbackGrid.RowCount = r + 1;
            foreach (var c in new[] { c1, c2 })
            {
                if (c is TextBox tb) { tb.Width = 190; tb.BorderStyle = BorderStyle.FixedSingle; tb.Anchor = AnchorStyles.Left; tb.Margin = new Padding(0, 4, 8, 4); }
            }
            fallbackGrid.Controls.Add(new Label { Text = l1, AutoSize = true, Anchor = AnchorStyles.Left }, 0, r);
            fallbackGrid.Controls.Add(c1, 1, r);
            fallbackGrid.Controls.Add(new Label { Text = l2, AutoSize = true, Anchor = AnchorStyles.Left }, 2, r);
            fallbackGrid.Controls.Add(c2, 3, r);
        }
        AddFallbackPair("Voucher type", _txtVoucherType, "Batch name", _txtBatchName);
        AddFallbackPair("Voucher class", _txtVoucherClass, "Company", _txtTallyCompanyName);
        var rOptional = fallbackGrid.RowCount;
        fallbackGrid.RowCount = rOptional + 1;
        fallbackGrid.Controls.Add(new Label { Text = "Push as Optional", AutoSize = true, Anchor = AnchorStyles.Left }, 0, rOptional);
        fallbackGrid.Controls.Add(_chkPushAsOptional, 1, rOptional);
        var lblFallbackNote = new Label
        {
            Text = "Ledger names, tax lines and company now come from the API per invoice — these only apply when it omits one.",
            AutoSize = true,
            ForeColor = UiTheme.TextMuted,
            Font = new Font("Segoe UI", 8f)
        };
        var rFallback = fallbackGrid.RowCount;
        fallbackGrid.RowCount = rFallback + 1;
        fallbackGrid.Controls.Add(lblFallbackNote, 0, rFallback);
        fallbackGrid.SetColumnSpan(lblFallbackNote, 4);

        // Schedule & Startup
        var schedGrid = MakeFieldGrid();
        _numPushInterval.Minimum = 1;
        _numPushInterval.Maximum = 1440;
        _numPushInterval.Value = 3;
        _numPushInterval.Width = 100;
        AddField(schedGrid, "Interval (min)", _numPushInterval);
        AddField(schedGrid, "Start minimized", _chkStartMinimized);
        AddField(schedGrid, "Start with Windows", _chkAutoStart);

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

        // Database (groundwork only — see AgentConfig.cs)
        var dbGrid = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 4, AutoSize = true };
        dbGrid.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 90));
        dbGrid.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50));
        dbGrid.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 90));
        dbGrid.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50));
        void AddDbPair(string l1, Control c1, string l2, Control c2)
        {
            var r = dbGrid.RowCount;
            dbGrid.RowCount = r + 1;
            foreach (var c in new[] { c1, c2 })
            {
                if (c is TextBox tb) { tb.Width = 190; tb.BorderStyle = BorderStyle.FixedSingle; tb.Anchor = AnchorStyles.Left; tb.Margin = new Padding(0, 4, 8, 4); }
            }
            dbGrid.Controls.Add(new Label { Text = l1, AutoSize = true, Anchor = AnchorStyles.Left }, 0, r);
            dbGrid.Controls.Add(c1, 1, r);
            dbGrid.Controls.Add(new Label { Text = l2, AutoSize = true, Anchor = AnchorStyles.Left }, 2, r);
            dbGrid.Controls.Add(c2, 3, r);
        }
        _txtSqlPassword.UseSystemPasswordChar = true;
        AddDbPair("Server", _txtSqlServer, "Database", _txtSqlDatabase);
        AddDbPair("User ID", _txtSqlUserId, "Password", _txtSqlPassword);

        _btnTestSql.Text = "Test Connection";
        UiTheme.StyleButton(_btnTestSql, UiTheme.Info);
        _btnTestSql.Click += async (_, _) => await TestSqlClicked();
        _lblSqlStatus = UiTheme.StatusChip("Not tested", UiTheme.TextMuted);
        var rDb = dbGrid.RowCount;
        dbGrid.RowCount = rDb + 1;
        dbGrid.Controls.Add(_btnTestSql, 1, rDb);
        dbGrid.Controls.Add(_lblSqlStatus, 3, rDb);
        var lblDbNote = new Label
        {
            Text = "Not used yet — groundwork for a future report-sync feature.",
            AutoSize = true,
            ForeColor = UiTheme.TextMuted,
            Font = new Font("Segoe UI", 8f),
            Margin = new Padding(0, 8, 0, 0)
        };
        var rNote = dbGrid.RowCount;
        dbGrid.RowCount = rNote + 1;
        dbGrid.Controls.Add(lblDbNote, 0, rNote);
        dbGrid.SetColumnSpan(lblDbNote, 4);

        var gbApi = MakeGroupBox("🌐  Sales API", apiGrid);
        var gbTally = MakeGroupBox("📇  Tally Connection", tallyGrid);
        var gbFallback = MakeGroupBox("🧾  Fallback Defaults", fallbackGrid);
        var gbSched = MakeGroupBox("🕐  Schedule && Startup", schedGrid);
        var gbLicense = MakeGroupBox("🔑  License", licenseGrid);
        gbLicense.ForeColor = UiTheme.Warning;
        var gbDb = MakeGroupBox("🗄  Database (optional)", dbGrid);
        gbDb.ForeColor = UiTheme.Info;

        int row = 0;
        outer.Controls.Add(gbApi, 0, row);
        outer.Controls.Add(gbTally, 1, row); row++;
        outer.Controls.Add(gbFallback, 0, row);
        outer.SetColumnSpan(gbFallback, 2); row++;
        outer.Controls.Add(gbSched, 0, row);
        outer.Controls.Add(gbLicense, 1, row); row++;
        outer.Controls.Add(gbDb, 0, row);
        outer.SetColumnSpan(gbDb, 2); row++;

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
        _txtAuthHeaderName.Text = _config.AuthHeaderName;
        _txtAuthScheme.Text = _config.AuthScheme;
        _txtTallyUrl.Text = _config.TallyUrl;
        _txtVoucherType.Text = _config.VoucherType;
        _txtBatchName.Text = _config.BatchName;
        _txtVoucherClass.Text = _config.VoucherClass;
        _txtTallyCompanyName.Text = _config.TallyCompanyName;
        _chkPushAsOptional.Checked = _config.PushAsOptional;
        _numPushInterval.Value = Math.Clamp(_config.PushIntervalMinutes, 1, 1440);
        _chkStartMinimized.Checked = _config.StartMinimized;
        _chkAutoStart.Checked = _config.AutoStartWithWindows;
        _txtSqlServer.Text = _config.SqlServer;
        _txtSqlDatabase.Text = _config.SqlDatabase;
        _txtSqlUserId.Text = _config.SqlUserId;
        _txtSqlPassword.Text = ConfigService.Unprotect(_config.SqlPasswordProtected);

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
    // like a fresh, never-activated install. Useful before moving the
    // install to another machine, or to force a clean re-activation if
    // something local got stuck.
    //
    // Earlier version opened FrmLicense as a dialog *from inside FrmMain*
    // instead of restarting — that left the Status/Config/Logs window
    // sitting open behind the license prompt, which isn't the same thing as
    // "the app is now unlicensed" (the agent was stopped, but the window
    // and everything else was still right there). A full process restart is
    // the only way to actually land on "just the license prompt, nothing
    // else running" — the same state a fresh install would be in.
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
        _config.ApiBaseUrl = _txtApiBaseUrl.Text.Trim();
        _config.ApiKeyProtected = ConfigService.Protect(_txtApiKey.Text);
        _config.AuthHeaderName = _txtAuthHeaderName.Text.Trim();
        _config.AuthScheme = _txtAuthScheme.Text.Trim();
        _config.TallyUrl = _txtTallyUrl.Text.Trim();
        _config.VoucherType = _txtVoucherType.Text.Trim();
        _config.BatchName = _txtBatchName.Text.Trim();
        _config.VoucherClass = _txtVoucherClass.Text.Trim();
        _config.TallyCompanyName = _txtTallyCompanyName.Text.Trim();
        _config.PushAsOptional = _chkPushAsOptional.Checked;
        _config.PushIntervalMinutes = (int)_numPushInterval.Value;
        _config.StartMinimized = _chkStartMinimized.Checked;
        _config.AutoStartWithWindows = _chkAutoStart.Checked;
        _config.SqlServer = _txtSqlServer.Text.Trim();
        _config.SqlDatabase = _txtSqlDatabase.Text.Trim();
        _config.SqlUserId = _txtSqlUserId.Text.Trim();
        _config.SqlPasswordProtected = ConfigService.Protect(_txtSqlPassword.Text);

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

    // Not wired to any sync feature yet (see AgentConfig.cs) — this just
    // proves the entered credentials can actually open a connection, ahead
    // of whatever gets built on top of them later.
    private async Task TestSqlClicked()
    {
        _btnTestSql.Enabled = false;
        _lblSqlStatus.ForeColor = UiTheme.TextMuted;
        _lblSqlStatus.Text = "● Testing…";
        try
        {
            var builder = new SqlConnectionStringBuilder
            {
                DataSource = _txtSqlServer.Text.Trim(),
                InitialCatalog = _txtSqlDatabase.Text.Trim(),
                UserID = _txtSqlUserId.Text.Trim(),
                Password = _txtSqlPassword.Text,
                TrustServerCertificate = true,
                ConnectTimeout = 5
            };
            await using var conn = new SqlConnection(builder.ConnectionString);
            await conn.OpenAsync();
            _lblSqlStatus.ForeColor = UiTheme.Success;
            _lblSqlStatus.Text = "● Connected";
        }
        catch (Exception ex)
        {
            _lblSqlStatus.ForeColor = UiTheme.Danger;
            _lblSqlStatus.Text = "● Failed — " + ex.Message;
        }
        finally
        {
            _btnTestSql.Enabled = true;
        }
    }

    // Builds a config snapshot from whatever's currently typed in the
    // Configuration tab, without requiring Save first — so Test buttons
    // reflect what the user is about to save.
    private AgentConfig CurrentFormConfig() => new()
    {
        ApiBaseUrl = _txtApiBaseUrl.Text.Trim(),
        ApiKeyProtected = ConfigService.Protect(_txtApiKey.Text),
        AuthHeaderName = _txtAuthHeaderName.Text.Trim(),
        AuthScheme = _txtAuthScheme.Text.Trim(),
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
