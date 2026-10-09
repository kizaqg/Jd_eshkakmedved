namespace JdClicker;

/// <summary>
/// Затемняет клиентскую область игры. Пользователь выделяет мышью строку ввода чата, Esc отменяет.
/// Результат сохраняется в координатах клиентской области окна игры.
/// </summary>
internal sealed class ChatAreaPicker : Form
{
    Point _start, _end;
    bool _selecting;

    public ChatArea? Result { get; private set; }

    public ChatAreaPicker(Rectangle clientOnScreen)
    {
        FormBorderStyle = FormBorderStyle.None;
        StartPosition = FormStartPosition.Manual;
        AutoScaleMode = AutoScaleMode.None;
        Bounds = clientOnScreen;
        TopMost = true;
        ShowInTaskbar = false;
        BackColor = Color.Black;
        Opacity = 0.45;
        Cursor = Cursors.Cross;
        DoubleBuffered = true;
        KeyPreview = true;
        Text = "Выделите поле ввода чата";
    }

    protected override void OnKeyDown(KeyEventArgs e)
    {
        if (e.KeyCode == Keys.Escape) { DialogResult = DialogResult.Cancel; Close(); }
        base.OnKeyDown(e);
    }

    protected override void OnMouseDown(MouseEventArgs e)
    {
        if (e.Button == MouseButtons.Right) { DialogResult = DialogResult.Cancel; Close(); return; }
        _selecting = true;
        _start = _end = e.Location;
        Invalidate();
    }

    protected override void OnMouseMove(MouseEventArgs e)
    {
        if (!_selecting) return;
        _end = e.Location;
        Invalidate();
    }

    protected override void OnMouseUp(MouseEventArgs e)
    {
        if (!_selecting) return;
        _selecting = false;
        var r = Selection();
        if (r.Width < 4 || r.Height < 4) { Invalidate(); return; }
        // Координаты формы уже относительно клиентской области игры (форма лежит ровно на ней).
        Result = new ChatArea { X = r.X, Y = r.Y, W = r.Width, H = r.Height };
        DialogResult = DialogResult.OK;
        Close();
    }

    Rectangle Selection() => Rectangle.FromLTRB(
        Math.Min(_start.X, _end.X), Math.Min(_start.Y, _end.Y),
        Math.Max(_start.X, _end.X), Math.Max(_start.Y, _end.Y));

    protected override void OnPaint(PaintEventArgs e)
    {
        var g = e.Graphics;
        const string hint = "Выделите мышью строку ввода чата (где печатается текст). Esc или правая кнопка — отмена.";
        using var font = new Font("Segoe UI", 12f, FontStyle.Bold);
        TextRenderer.DrawText(g, hint, font, new Point(20, 20), Color.White);
        if (_selecting || _start != _end)
        {
            var r = Selection();
            using var fill = new SolidBrush(Color.FromArgb(255, 60, 160, 255));
            using var pen = new Pen(Color.White, 2);
            g.FillRectangle(fill, r);
            g.DrawRectangle(pen, r);
        }
    }
}
