namespace JdClicker;

/// <summary>
/// Плашка-индикатор поверх игры. Фокус не забирает (WS_EX_NOACTIVATE),
/// перетаскивается левой кнопкой, правая кнопка открывает меню.
/// </summary>
internal sealed class OverlayForm : Form
{
    const int WS_EX_TOPMOST = 0x8, WS_EX_TOOLWINDOW = 0x80, WS_EX_NOACTIVATE = 0x08000000;
    const int WM_MOUSEACTIVATE = 0x21, MA_NOACTIVATE = 3;
    const int WM_NCHITTEST = 0x84, HTCLIENT = 1, HTCAPTION = 2;
    const int WM_NCRBUTTONUP = 0xA5, WM_ENTERSIZEMOVE = 0x231, WM_EXITSIZEMOVE = 0x232;

    readonly ContextMenuStrip _menu;
    Color _dot = Color.Gray;
    string _text = "";

    public bool Dragging { get; private set; }
    public event Action? DragFinished;

    public OverlayForm(ContextMenuStrip menu)
    {
        _menu = menu;
        FormBorderStyle = FormBorderStyle.None;
        ShowInTaskbar = false;
        StartPosition = FormStartPosition.Manual;
        TopMost = true;
        AutoScaleMode = AutoScaleMode.None;
        BackColor = Color.FromArgb(24, 24, 28);
        ForeColor = Color.White;
        Opacity = 0.88;
        DoubleBuffered = true;
        Font = new Font("Segoe UI", 9f, FontStyle.Bold);
        Text = "JdClicker";
    }

    protected override bool ShowWithoutActivation => true;

    protected override CreateParams CreateParams
    {
        get
        {
            var cp = base.CreateParams;
            cp.ExStyle |= WS_EX_TOPMOST | WS_EX_TOOLWINDOW | WS_EX_NOACTIVATE;
            return cp;
        }
    }

    protected override void WndProc(ref Message m)
    {
        switch (m.Msg)
        {
            case WM_MOUSEACTIVATE:
                m.Result = (IntPtr)MA_NOACTIVATE;
                return;
            case WM_NCHITTEST:
                base.WndProc(ref m);
                if ((int)m.Result == HTCLIENT) m.Result = (IntPtr)HTCAPTION;
                return;
            case WM_NCRBUTTONUP:
                _menu.Show(Cursor.Position);
                return;
            case WM_ENTERSIZEMOVE:
                Dragging = true;
                break;
            case WM_EXITSIZEMOVE:
                Dragging = false;
                DragFinished?.Invoke();
                break;
        }
        base.WndProc(ref m);
    }

    public void SetStatus(Color dot, string text)
    {
        if (dot == _dot && text == _text) return;
        _dot = dot;
        _text = text;
        float k = DeviceDpi / 96f;
        var size = TextRenderer.MeasureText(text, Font);
        ClientSize = new Size((int)(26 * k) + size.Width, Math.Max((int)(22 * k), size.Height + (int)(6 * k)));
        Invalidate();
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        float k = DeviceDpi / 96f;
        var g = e.Graphics;
        g.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias;
        int d = (int)(12 * k);
        int y = (ClientSize.Height - d) / 2;
        using (var b = new SolidBrush(_dot)) g.FillEllipse(b, (int)(7 * k), y, d, d);
        using (var p = new Pen(Color.FromArgb(160, 0, 0, 0))) g.DrawEllipse(p, (int)(7 * k), y, d, d);
        var r = new Rectangle((int)(24 * k), 0, ClientSize.Width - (int)(24 * k), ClientSize.Height);
        TextRenderer.DrawText(g, _text, Font, r, ForeColor, TextFormatFlags.VerticalCenter | TextFormatFlags.Left);
        using var border = new Pen(Color.FromArgb(70, 70, 80));
        g.DrawRectangle(border, 0, 0, ClientSize.Width - 1, ClientSize.Height - 1);
    }
}
