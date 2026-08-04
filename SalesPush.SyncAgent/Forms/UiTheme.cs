namespace SalesPush.SyncAgent.Forms;

// Small shared palette/helpers so FrmMain and FrmLicense look like one
// product instead of two differently-styled forms. Plain flat-design colors
// rather than a full custom-control library — this is a code-only WinForms
// project (no Designer.cs anywhere), so anything fancier than flat colors +
// icon glyphs would be a lot of GDI+ owner-draw work for little payoff.
internal static class UiTheme
{
    public static readonly Color Primary = ColorTranslator.FromHtml("#4F46E5");
    public static readonly Color PrimaryDark = ColorTranslator.FromHtml("#4338CA");
    public static readonly Color Success = ColorTranslator.FromHtml("#16A34A");
    public static readonly Color Danger = ColorTranslator.FromHtml("#DC2626");
    public static readonly Color Warning = ColorTranslator.FromHtml("#D97706");
    public static readonly Color Info = ColorTranslator.FromHtml("#0891B2");
    public static readonly Color Background = ColorTranslator.FromHtml("#F3F4F6");
    public static readonly Color Card = Color.White;
    public static readonly Color Border = ColorTranslator.FromHtml("#E5E7EB");
    public static readonly Color TextMuted = ColorTranslator.FromHtml("#6B7280");
    public static readonly Color TextDark = ColorTranslator.FromHtml("#111827");

    public static void StyleButton(Button b, Color backColor, Color? foreColor = null)
    {
        b.FlatStyle = FlatStyle.Flat;
        b.FlatAppearance.BorderSize = 0;
        b.FlatAppearance.MouseOverBackColor = ControlPaint.Light(backColor, 0.15f);
        b.FlatAppearance.MouseDownBackColor = ControlPaint.Dark(backColor, 0.1f);
        b.BackColor = backColor;
        b.ForeColor = foreColor ?? Color.White;
        b.Font = new Font("Segoe UI", 9.5f, FontStyle.Bold);
        b.Cursor = Cursors.Hand;
        b.Padding = new Padding(10, 6, 10, 6);
        b.AutoSize = true;
        b.MinimumSize = new Size(0, 34);
    }

    public static Panel CreateCard(string? title = null)
    {
        var card = new Panel
        {
            BackColor = Card,
            Padding = new Padding(20, title == null ? 20 : 44, 20, 20)
        };
        card.Paint += (_, e) =>
        {
            using var pen = new Pen(Border);
            e.Graphics.DrawRectangle(pen, 0, 0, card.Width - 1, card.Height - 1);
        };
        if (title != null)
        {
            var lbl = new Label
            {
                Text = title,
                Font = new Font("Segoe UI", 11f, FontStyle.Bold),
                ForeColor = Primary,
                AutoSize = true,
                Location = new Point(20, 14)
            };
            card.Controls.Add(lbl);
        }
        return card;
    }

    public static Label StatusChip(string text, Color color)
    {
        return new Label
        {
            Text = "● " + text,
            AutoSize = true,
            ForeColor = color,
            Font = new Font("Segoe UI", 9.5f, FontStyle.Bold)
        };
    }
}
