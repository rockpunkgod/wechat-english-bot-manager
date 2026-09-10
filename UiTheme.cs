using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Windows.Forms;

namespace WechatBotDesktop {
public static class UiTheme {
    public static readonly Color Canvas = Color.FromArgb(244, 248, 246);
    public static readonly Color Ink = Color.FromArgb(31, 52, 43);
    public static readonly Color Muted = Color.FromArgb(107, 124, 114);
    public static readonly Color Green = Color.FromArgb(26, 124, 82);
    public static readonly Color Line = Color.FromArgb(223, 232, 226);
    public static GraphicsPath Rounded(RectangleF r, float radius) {
        var p = new GraphicsPath(); float d = radius * 2;
        p.AddArc(r.X, r.Y, d, d, 180, 90); p.AddArc(r.Right - d, r.Y, d, d, 270, 90);
        p.AddArc(r.Right - d, r.Bottom - d, d, d, 0, 90); p.AddArc(r.X, r.Bottom - d, d, d, 90, 90); p.CloseFigure(); return p;
    }
    public static void StyleGrid(DataGridView grid) {
        grid.BackgroundColor = Color.White; grid.BorderStyle = BorderStyle.None; grid.RowHeadersVisible = false;
        grid.EnableHeadersVisualStyles = false; grid.ColumnHeadersHeight = 42; grid.ColumnHeadersHeightSizeMode = DataGridViewColumnHeadersHeightSizeMode.DisableResizing;
        grid.ColumnHeadersDefaultCellStyle.BackColor = Color.FromArgb(233, 241, 237); grid.ColumnHeadersDefaultCellStyle.ForeColor = Ink;
        grid.ColumnHeadersDefaultCellStyle.Font = new Font("Microsoft YaHei UI", 9F, FontStyle.Regular);
        grid.ColumnHeadersDefaultCellStyle.Padding = new Padding(9, 0, 0, 0);
        grid.ColumnHeadersDefaultCellStyle.WrapMode = DataGridViewTriState.False;
        grid.DefaultCellStyle.Font = new Font("Microsoft YaHei UI", 9F); grid.DefaultCellStyle.ForeColor = Ink; grid.DefaultCellStyle.Padding = new Padding(9, 0, 4, 0);
        grid.DefaultCellStyle.SelectionBackColor = Color.FromArgb(222, 241, 231); grid.DefaultCellStyle.SelectionForeColor = Ink;
        grid.AlternatingRowsDefaultCellStyle.BackColor = Color.FromArgb(250, 252, 251); grid.GridColor = Color.FromArgb(239, 244, 241);
        grid.CellBorderStyle = DataGridViewCellBorderStyle.SingleHorizontal; grid.RowTemplate.Height = 46;
    }
    public static string ButtonIcon(string label) {
        if (label.Contains("新增")) return "add";
        if (label.Contains("启动")) return "play";
        if (label.Contains("停止")) return "stop";
        if (label.Contains("扫码")) return "qr";
        if (label.Contains("用量")) return "chart";
        if (label.Contains("接管")) return "handoff";
        if (label.Contains("改名")) return "edit";
        if (label.Contains("停用")) return "toggle";
        if (label.Contains("移除") && !label.Contains("恢复")) return "archive";
        if (label.Contains("恢复")) return "restore";
        if (label.Contains("目录")) return "folder";
        if (label.Contains("Codex") || label.Contains("源码")) return "code";
        if (label.Contains("规则")) return "book";
        if (label.Contains("取消")) return "close";
        if (label.Contains("刷新")) return "refresh";
        if (label.Contains("管理")) return "bots";
        return "check";
    }
}
public static class UiIcons {
    public static Bitmap Bitmap(string icon) { var bitmap = new Bitmap(20, 20); using (var g = Graphics.FromImage(bitmap)) Draw(g, icon, new Rectangle(0, 0, 20, 20), UiTheme.Green); return bitmap; }
    public static void Draw(Graphics g, string icon, Rectangle bounds, Color color) {
        var state = g.Save(); g.TranslateTransform(bounds.X, bounds.Y); g.ScaleTransform(bounds.Width / 24F, bounds.Height / 24F); g.SmoothingMode = SmoothingMode.AntiAlias;
        using (var p = new Pen(color, 1.7F) { StartCap = LineCap.Round, EndCap = LineCap.Round, LineJoin = LineJoin.Round })
        using (var brush = new SolidBrush(color)) {
            switch (icon) {
            case "add": g.DrawLine(p, 12, 5, 12, 19); g.DrawLine(p, 5, 12, 19, 12); break;
            case "play": g.DrawPolygon(p, new[] { new PointF(8, 5), new PointF(19, 12), new PointF(8, 19) }); break;
            case "stop": using (var r = UiTheme.Rounded(new RectangleF(5, 5, 14, 14), 2)) g.DrawPath(p, r); break;
            case "qr": g.DrawRectangle(p, 3, 3, 6, 6); g.DrawRectangle(p, 15, 3, 6, 6); g.DrawRectangle(p, 3, 15, 6, 6); g.DrawLine(p, 15, 14, 15, 18); g.DrawLine(p, 15, 18, 19, 18); g.DrawLine(p, 21, 14, 21, 21); g.DrawLine(p, 12, 21, 16, 21); break;
            case "chart": g.DrawLine(p, 4, 4, 4, 20); g.DrawLine(p, 4, 20, 21, 20); g.DrawLine(p, 8, 15, 8, 11); g.DrawLine(p, 13, 15, 13, 6); g.DrawLine(p, 18, 15, 18, 9); break;
            case "handoff": g.DrawLine(p, 4, 8, 20, 8); g.DrawLines(p, new[] { new Point(16,4), new Point(20,8), new Point(16,12) }); g.DrawLine(p, 20, 17, 4, 17); g.DrawLines(p, new[] { new Point(8,13), new Point(4,17), new Point(8,21) }); break;
            case "edit": g.DrawPolygon(p, new[] { new Point(4,16), new Point(16,4), new Point(20,8), new Point(8,20), new Point(3,21) }); g.DrawLine(p, 13, 7, 17, 11); break;
            case "toggle": using (var r = UiTheme.Rounded(new RectangleF(2, 6, 20, 12), 6)) g.DrawPath(p, r); g.FillEllipse(brush, 13, 9, 6, 6); break;
            case "archive": g.DrawRectangle(p, 3, 4, 18, 5); g.DrawRectangle(p, 5, 9, 14, 11); g.DrawLine(p, 9, 13, 15, 13); break;
            case "restore": g.DrawArc(p, 4, 4, 16, 16, 200, 295); g.DrawLines(p, new[] { new Point(3,3), new Point(3,9), new Point(9,9) }); g.DrawLine(p, 12, 8, 12, 13); g.DrawLine(p, 12, 13, 16, 13); break;
            case "folder": g.DrawPolygon(p, new[] { new Point(3,6), new Point(9,6), new Point(11,9), new Point(21,9), new Point(21,20), new Point(3,20) }); g.DrawLine(p, 3, 12, 21, 12); break;
            case "code": g.DrawLines(p, new[] { new Point(8,6), new Point(2,12), new Point(8,18) }); g.DrawLines(p, new[] { new Point(16,6), new Point(22,12), new Point(16,18) }); g.DrawLine(p, 14, 4, 10, 20); break;
            case "book": g.DrawPolygon(p, new[] { new Point(3,4), new Point(9,4), new Point(12,6), new Point(15,4), new Point(21,4), new Point(21,19), new Point(15,19), new Point(12,21), new Point(9,19), new Point(3,19) }); g.DrawLine(p, 12, 6, 12, 21); break;
            case "close": g.DrawLine(p, 6, 6, 18, 18); g.DrawLine(p, 18, 6, 6, 18); break;
            case "refresh": g.DrawArc(p, 4, 4, 16, 16, 45, 290); g.DrawLines(p, new[] { new Point(15,3), new Point(21,3), new Point(21,9) }); break;
            case "bots": using (var r = UiTheme.Rounded(new RectangleF(4, 7, 16, 13), 3)) g.DrawPath(p, r); g.DrawLine(p, 12, 3, 12, 7); g.FillEllipse(brush, 8, 11, 2.6F, 2.6F); g.FillEllipse(brush, 14, 11, 2.6F, 2.6F); g.DrawLine(p, 9, 17, 15, 17); break;
            case "image": g.DrawRectangle(p, 3, 4, 18, 16); g.DrawEllipse(p, 14, 7, 3, 3); g.DrawLines(p, new[] { new Point(4,18), new Point(10,11), new Point(16,18), new Point(20,14) }); break;
            case "money": g.DrawEllipse(p, 3, 3, 18, 18); g.DrawLine(p, 8, 7, 12, 12); g.DrawLine(p, 16, 7, 12, 12); g.DrawLine(p, 8, 12, 16, 12); g.DrawLine(p, 8, 15, 16, 15); g.DrawLine(p, 12, 12, 12, 18); break;
            case "in": g.DrawLine(p, 12, 3, 12, 16); g.DrawLines(p, new[] { new Point(7,11), new Point(12,16), new Point(17,11) }); g.DrawLines(p, new[] { new Point(4,16), new Point(4,21), new Point(20,21), new Point(20,16) }); break;
            case "out": g.DrawLine(p, 12, 17, 12, 4); g.DrawLines(p, new[] { new Point(7,9), new Point(12,4), new Point(17,9) }); g.DrawLines(p, new[] { new Point(4,16), new Point(4,21), new Point(20,21), new Point(20,16) }); break;
            default: g.DrawLines(p, new[] { new Point(4,12), new Point(10,18), new Point(20,6) }); break;
            }
        }
        g.Restore(state);
    }
}
public class UiActionButton : Button {
    bool hover, pressed, primary;
    public string IconKey;
    public bool Primary { get { return primary; } set { primary = value; Invalidate(); } }
    public UiActionButton(string text, string icon, bool primary) {
        Text = text.Replace("＋ ", ""); IconKey = icon; this.primary = primary;
        Font = new Font("Microsoft YaHei UI", 9.5F); Height = 38;
        Width = Math.Max(88, TextRenderer.MeasureText(Text, Font).Width + 48);
        AutoSize = false; FlatStyle = FlatStyle.Flat; FlatAppearance.BorderSize = 0; Margin = new Padding(0, 0, 9, 8); Cursor = Cursors.Hand;
        SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true);
    }
    protected override void OnMouseEnter(EventArgs e) { hover = true; Invalidate(); base.OnMouseEnter(e); }
    protected override void OnMouseLeave(EventArgs e) { hover = false; pressed = false; Invalidate(); base.OnMouseLeave(e); }
    protected override void OnMouseDown(MouseEventArgs e) { pressed = true; Invalidate(); base.OnMouseDown(e); }
    protected override void OnMouseUp(MouseEventArgs e) { pressed = false; Invalidate(); base.OnMouseUp(e); }
    protected override void OnPaint(PaintEventArgs e) {
        var g = e.Graphics; g.Clear(Parent == null ? UiTheme.Canvas : Parent.BackColor); g.SmoothingMode = SmoothingMode.AntiAlias;
        float scale = g.DpiX / 96F;
        Color fill = primary ? (pressed ? Color.FromArgb(18, 91, 59) : hover ? Color.FromArgb(24, 140, 91) : UiTheme.Green) : (hover ? Color.FromArgb(230, 241, 234) : Color.White);
        Color ink = !Enabled ? Color.FromArgb(163, 175, 168) : primary ? Color.White : UiTheme.Ink;
        if (!Enabled) fill = Color.FromArgb(237, 241, 239);
        using (var shape = UiTheme.Rounded(new RectangleF(0.7F, 0.7F, Width - 1.4F, Height - 1.4F), 8))
        using (var brush = new SolidBrush(fill)) using (var pen = new Pen(primary ? fill : UiTheme.Line)) { g.FillPath(brush, shape); g.DrawPath(pen, shape); }
        int iconSize = (int)(19 * scale), textLeft = (int)(36 * scale);
        UiIcons.Draw(g, IconKey, new Rectangle((int)(12 * scale), (Height - iconSize) / 2, iconSize, iconSize), ink);
        TextRenderer.DrawText(g, Text, Font, new Rectangle(textLeft, 0, Width - textLeft - (int)(5 * scale), Height), ink, TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis);
        if (Focused && ShowFocusCues) ControlPaint.DrawFocusRectangle(g, new Rectangle(4, 4, Width - 8, Height - 8), ink, fill);
    }
}
public class MetricCard : Control {
    public string Caption, ValueText = "—", Note = "", IconKey;
    public MetricCard(string caption, string icon) { Caption = caption; IconKey = icon; DoubleBuffered = true; Dock = DockStyle.Fill; Margin = new Padding(0, 0, 10, 0); }
    protected override void OnPaint(PaintEventArgs e) {
        var g = e.Graphics; g.Clear(Parent == null ? UiTheme.Canvas : Parent.BackColor); g.SmoothingMode = SmoothingMode.AntiAlias;
        float scale = g.DpiX / 96F;
        using (var shape = UiTheme.Rounded(new RectangleF(0.5F, 0.5F, Width - 1, Height - 1), 12)) using (var b = new SolidBrush(Color.White)) using (var p = new Pen(UiTheme.Line)) { g.FillPath(b, shape); g.DrawPath(p, shape); }
        UiIcons.Draw(g, IconKey, new Rectangle((int)(15 * scale), (int)(16 * scale), (int)(19 * scale), (int)(19 * scale)), UiTheme.Green);
        float size = ValueText.Length > 9 ? 14F : ValueText.Length > 7 ? 17F : 20F;
        using (var label = new Font("Microsoft YaHei UI", 9F)) using (var value = new Font("Segoe UI", size, FontStyle.Bold)) using (var small = new Font("Microsoft YaHei UI", 8F)) {
            TextRenderer.DrawText(g, Caption, label, new Rectangle((int)(40 * scale), (int)(15 * scale), Width - (int)(45 * scale), (int)(23 * scale)), UiTheme.Muted, TextFormatFlags.EndEllipsis);
            TextRenderer.DrawText(g, ValueText, value, new Rectangle((int)(14 * scale), (int)(45 * scale), Width - (int)(25 * scale), (int)(39 * scale)), UiTheme.Ink, TextFormatFlags.EndEllipsis);
            TextRenderer.DrawText(g, Note, small, new Rectangle((int)(16 * scale), (int)(89 * scale), Width - (int)(28 * scale), (int)(30 * scale)), UiTheme.Muted, TextFormatFlags.EndEllipsis);
        }
    }
}
}
