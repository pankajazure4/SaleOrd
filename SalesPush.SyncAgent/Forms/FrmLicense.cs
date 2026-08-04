using SalesPush.SyncAgent.Licensing;
using SalesPush.SyncAgent.Models;
using SalesPush.SyncAgent.Services;

namespace SalesPush.SyncAgent.Forms;

// Blocks app startup until a valid license is activated. Shown via
// ShowDialog() from Program.cs before FrmMain is ever created — if this
// closes with anything other than DialogResult.OK, the process exits without
// starting the agent. Re-shown from FrmMain's Configuration tab ("Change
// License Key") to let an already-activated install switch keys later.
public class FrmLicense : Form
{
    private readonly LicenseService _licenseSvc;
    private readonly AgentConfig _config;

    private readonly TextBox _txtMachineId = new();
    private readonly Label _lblCurrentKey = new();
    private readonly TextBox _txtNewKey = new();
    private readonly Label _lblStatus = new();
    private readonly Button _btnActivate = new();
    private readonly Button _btnExit = new();

    public FrmLicense(LicenseService licenseSvc, AgentConfig config)
    {
        _licenseSvc = licenseSvc;
        _config = config;

        Text = "SalesPush Sync Agent — License Activation";
        Width = 520;
        Height = 460;
        StartPosition = FormStartPosition.CenterScreen;
        FormBorderStyle = FormBorderStyle.FixedDialog;
        MaximizeBox = false;
        MinimizeBox = false;
        BackColor = UiTheme.Background;

        try { Icon = new Icon(Path.Combine(AppContext.BaseDirectory, "agent.ico")); }
        catch { /* fall back to default icon */ }

        BuildUi();
        Load += FrmLicense_Load;
    }

    private void BuildUi()
    {
        var card = UiTheme.CreateCard("🔑 Activate SalesPush Sync Agent");
        card.Dock = DockStyle.Fill;
        card.Margin = new Padding(16);

        var outer = new Panel { Dock = DockStyle.Fill, Padding = new Padding(16) };
        outer.Controls.Add(card);
        Controls.Add(outer);

        var layout = new TableLayoutPanel
        {
            Dock = DockStyle.Top,
            ColumnCount = 1,
            AutoSize = true,
            Top = 50,
            Location = new Point(0, 50)
        };
        layout.Width = card.Width;

        var lblIntro = new Label
        {
            Text = "This machine's ID is below — send it to your vendor to get a license key issued for it.",
            AutoSize = false,
            Width = 440,
            Height = 40,
            ForeColor = UiTheme.TextMuted,
            Font = new Font("Segoe UI", 9f)
        };

        var lblMachineIdLabel = new Label { Text = "Machine ID", AutoSize = true, Font = new Font("Segoe UI", 9f, FontStyle.Bold), Top = 8 };
        _txtMachineId.ReadOnly = true;
        _txtMachineId.Width = 440;
        _txtMachineId.BackColor = Color.WhiteSmoke;

        var btnCopy = new Button { Text = "📋 Copy Machine ID", AutoSize = true };
        UiTheme.StyleButton(btnCopy, UiTheme.Info);
        btnCopy.Click += (_, _) =>
        {
            try { Clipboard.SetText(_txtMachineId.Text); _lblStatus.ForeColor = UiTheme.Success; _lblStatus.Text = "Machine ID copied to clipboard."; }
            catch { /* clipboard can be locked by another app — non-fatal */ }
        };

        var lblCurrentKeyLabel = new Label { Text = "Current license key", AutoSize = true, Font = new Font("Segoe UI", 9f, FontStyle.Bold) };
        _lblCurrentKey.Text = "(none activated)";
        _lblCurrentKey.AutoSize = true;
        _lblCurrentKey.ForeColor = UiTheme.TextMuted;
        _lblCurrentKey.Font = new Font("Consolas", 10f);

        var lblNewKeyLabel = new Label { Text = "License key", AutoSize = true, Font = new Font("Segoe UI", 9f, FontStyle.Bold) };
        _txtNewKey.Width = 440;
        _txtNewKey.UseSystemPasswordChar = true;
        _txtNewKey.PlaceholderText = "Paste the key you received";

        _btnActivate.Text = "✓ Activate";
        UiTheme.StyleButton(_btnActivate, UiTheme.Success);
        _btnActivate.Click += async (_, _) => await ActivateClicked();

        _btnExit.Text = "Exit";
        UiTheme.StyleButton(_btnExit, UiTheme.TextMuted);
        _btnExit.Click += (_, _) => { DialogResult = DialogResult.Cancel; Close(); };

        _lblStatus.AutoSize = false;
        _lblStatus.Width = 440;
        _lblStatus.Height = 40;
        _lblStatus.ForeColor = UiTheme.Danger;
        _lblStatus.Font = new Font("Segoe UI", 9f);

        var buttonRow = new FlowLayoutPanel { AutoSize = true, FlowDirection = FlowDirection.LeftToRight };
        buttonRow.Controls.Add(_btnActivate);
        buttonRow.Controls.Add(_btnExit);

        var stack = new FlowLayoutPanel
        {
            FlowDirection = FlowDirection.TopDown,
            AutoSize = true,
            Location = new Point(20, 44),
            WrapContents = false
        };
        stack.Controls.Add(lblIntro);
        stack.Controls.Add(lblMachineIdLabel);
        stack.Controls.Add(_txtMachineId);
        stack.Controls.Add(btnCopy);
        stack.Controls.Add(new Label { Height = 10 });
        stack.Controls.Add(lblCurrentKeyLabel);
        stack.Controls.Add(_lblCurrentKey);
        stack.Controls.Add(new Label { Height = 10 });
        stack.Controls.Add(lblNewKeyLabel);
        stack.Controls.Add(_txtNewKey);
        stack.Controls.Add(new Label { Height = 12 });
        stack.Controls.Add(buttonRow);
        stack.Controls.Add(_lblStatus);

        card.Controls.Add(stack);
    }

    private async void FrmLicense_Load(object? sender, EventArgs e)
    {
        _txtMachineId.Text = LicenseService.GetMachineId();

        var existingKey = LicenseService.GetLicenseKey(_config);
        _lblCurrentKey.Text = string.IsNullOrEmpty(existingKey) ? "(none activated)" : Licensing.LicenseKeyMask.Mask(existingKey);

        if (!string.IsNullOrWhiteSpace(existingKey))
        {
            // Already has a key from a previous run — try it silently before
            // making the user do anything; only stay open if it fails.
            _lblStatus.ForeColor = UiTheme.TextMuted;
            _lblStatus.Text = "Checking existing license…";
            var result = await _licenseSvc.CheckAsync(_config);
            if (result.IsValid)
            {
                DialogResult = DialogResult.OK;
                Close();
                return;
            }
            _lblStatus.ForeColor = UiTheme.Danger;
            _lblStatus.Text = result.Message;
        }
    }

    private async Task ActivateClicked()
    {
        _btnActivate.Enabled = false;
        _lblStatus.ForeColor = UiTheme.TextMuted;
        _lblStatus.Text = "Validating…";
        try
        {
            var typed = _txtNewKey.Text.Trim();
            if (!string.IsNullOrEmpty(typed))
                _licenseSvc.SaveLicenseKey(_config, typed);
            else
                _licenseSvc.Invalidate(); // re-check whatever key is already stored

            var result = await _licenseSvc.CheckAsync(_config);
            if (result.IsValid)
            {
                _lblCurrentKey.Text = Licensing.LicenseKeyMask.Mask(LicenseService.GetLicenseKey(_config));
                _txtNewKey.Clear();
                _lblStatus.ForeColor = UiTheme.Success;
                _lblStatus.Text = "License activated.";
                DialogResult = DialogResult.OK;
                Close();
            }
            else
            {
                _lblStatus.ForeColor = UiTheme.Danger;
                _lblStatus.Text = result.Message;
            }
        }
        finally
        {
            _btnActivate.Enabled = true;
        }
    }
}
