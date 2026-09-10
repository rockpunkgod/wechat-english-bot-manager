using System;
using System.Collections.Generic;
using System.Drawing;
using System.Globalization;
using System.Linq;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace WechatBotDesktop {
public class UsagePanel : UserControl {
    class BotChoice {
        public string Id, Label;
        public override string ToString() { return Label; }
    }
    readonly Store store;
    readonly bool preview;
    readonly UsageReader reader = new UsageReader();
    readonly ComboBox botFilter = new ComboBox(), periodFilter = new ComboBox();
    readonly CheckBox archived = new CheckBox();
    readonly Label rateNote = new Label(), footer = new Label(), empty = new Label();
    readonly DataGridView records = new DataGridView();
    readonly UiActionButton refresh = new UiActionButton("刷新记录", "refresh", false);
    readonly MetricCard requests = new MetricCard("已记录请求", "bots"), total = new MetricCard("总 Token", "chart"), input = new MetricCard("输入 Token", "in"), output = new MetricCard("输出 Token", "out"), pictures = new MetricCard("收到的图片", "image"), cost = new MetricCard("API 等价成本", "money");
    readonly System.Windows.Forms.Timer timer = new System.Windows.Forms.Timer();
    readonly ToolTip tips = new ToolTip { AutoPopDelay = 16000 };
    UsageSnapshot snapshot;
    bool updating, loading, refreshPending;

    public UsagePanel(Store store, bool preview) {
        this.store = store; this.preview = preview;
        SuspendLayout(); AutoScaleDimensions = new SizeF(96, 96); AutoScaleMode = AutoScaleMode.Dpi;
        Dock = DockStyle.Fill; BackColor = UiTheme.Canvas; Font = new Font("Microsoft YaHei UI", 9.5F);
        var layout = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 7, Margin = new Padding(0) };
        foreach (int height in new[] { 54, 48, 132, 62, 32 }) layout.RowStyles.Add(new RowStyle(SizeType.Absolute, height));
        layout.RowStyles.Add(new RowStyle(SizeType.Percent, 100)); layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 46)); Controls.Add(layout);
        var heading = new Panel { Dock = DockStyle.Fill, Margin = new Padding(0) };
        heading.Controls.Add(new Label { Text = "用量概览", Font = new Font(Font.FontFamily, 18, FontStyle.Bold), ForeColor = UiTheme.Ink, AutoSize = true, Location = new Point(0, 0) });
        heading.Controls.Add(new Label { Text = "本地统计 · 无需打开浏览器 · 查看统计不消耗模型额度", ForeColor = UiTheme.Muted, AutoSize = true, Location = new Point(166, 12) });
        layout.Controls.Add(heading, 0, 0);
        var filters = new FlowLayoutPanel { Dock = DockStyle.Fill, WrapContents = false, Margin = new Padding(0) };
        filters.Controls.Add(FilterLabel("账号")); botFilter.DropDownStyle = ComboBoxStyle.DropDownList; botFilter.Width = 206; botFilter.Margin = new Padding(0, 5, 20, 0); filters.Controls.Add(botFilter);
        filters.Controls.Add(FilterLabel("时间")); periodFilter.DropDownStyle = ComboBoxStyle.DropDownList; periodFilter.Width = 132; periodFilter.Margin = new Padding(0, 5, 18, 0); periodFilter.Items.AddRange(new object[] { "今天", "近 7 天", "近 30 天", "全部已保存" }); periodFilter.SelectedIndex = 1; filters.Controls.Add(periodFilter);
        archived.Text = "包含已移除 Bot"; archived.AutoSize = true; archived.Margin = new Padding(0, 7, 18, 0); filters.Controls.Add(archived); filters.Controls.Add(refresh); layout.Controls.Add(filters, 0, 1);
        var cards = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 6, RowCount = 1, Margin = new Padding(0, 0, 0, 8) };
        foreach (var card in new[] { requests, total, input, output, pictures, cost }) { cards.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100F / 6)); cards.Controls.Add(card); }
        cost.Margin = new Padding(0); layout.Controls.Add(cards, 0, 2);
        rateNote.Dock = DockStyle.Fill; rateNote.BackColor = Color.FromArgb(233, 242, 236); rateNote.ForeColor = UiTheme.Muted; rateNote.Padding = new Padding(12, 8, 8, 4); rateNote.Margin = new Padding(0, 0, 0, 8); rateNote.Font = new Font(Font.FontFamily, 8.5F); layout.Controls.Add(rateNote, 0, 3);
        layout.Controls.Add(new Label { Text = "请求明细", Font = new Font(Font.FontFamily, 11, FontStyle.Bold), ForeColor = UiTheme.Ink, AutoSize = true, Margin = new Padding(0, 1, 0, 0) }, 0, 4);
        var tableArea = new Panel { Dock = DockStyle.Fill, Margin = new Padding(0), BackColor = Color.White };
        records.Dock = DockStyle.Fill; records.AutoGenerateColumns = false; records.ReadOnly = true; records.AllowUserToAddRows = false; records.AllowUserToDeleteRows = false; records.AllowUserToResizeRows = false; records.SelectionMode = DataGridViewSelectionMode.FullRowSelect; records.MultiSelect = false;
        UiTheme.StyleGrid(records); records.RowTemplate.Height = 40;
        AddColumn("at", "时间", 130); AddColumn("bot", "Bot", 105); AddColumn("model", "模型", 130);
        AddColumn("input", "输入", 88); AddColumn("cached", "缓存输入", 88); AddColumn("output", "输出", 74); AddColumn("total", "总 Token", 88); AddColumn("images", "图片", 54); AddColumn("seconds", "耗时 / 秒", 78); AddColumn("cny", "人民币估算", 102); AddColumn("usd", "美元估算", 100); AddColumn("source", "统计来源", 93);
        tableArea.Controls.Add(records);
        empty.Dock = DockStyle.Fill; empty.BackColor = Color.White; empty.ForeColor = UiTheme.Muted; empty.Font = new Font(Font.FontFamily, 11F); empty.TextAlign = ContentAlignment.MiddleCenter; empty.Text = "选择账号后读取本地用量"; tableArea.Controls.Add(empty); empty.BringToFront();
        layout.Controls.Add(tableArea, 0, 5);
        footer.Dock = DockStyle.Fill; footer.ForeColor = UiTheme.Muted; footer.Font = new Font(Font.FontFamily, 8.5F); footer.Padding = new Padding(0, 7, 0, 0); footer.Text = "每个 Bot 最多保留最近 1,000 条记录；历史已清理的记录不在统计范围内。"; layout.Controls.Add(footer, 0, 6);
        botFilter.SelectedIndexChanged += (s, e) => { if (!updating && Visible) RefreshRecords(); };
        periodFilter.SelectedIndexChanged += (s, e) => { if (!updating) Render(); };
        archived.CheckedChanged += (s, e) => { RefreshBotChoices(); if (Visible) RefreshRecords(); };
        refresh.Click += (s, e) => RefreshRecords();
        VisibleChanged += (s, e) => { if (Visible && !preview) { RefreshRecords(); timer.Start(); } else timer.Stop(); };
        timer.Interval = 15000; timer.Tick += (s, e) => { if (Visible && !loading) RefreshRecords(); };
        RefreshBotChoices();
        ResumeLayout(false);
    }
    static Label FilterLabel(string text) { return new Label { Text = text, AutoSize = true, ForeColor = UiTheme.Muted, Margin = new Padding(0, 9, 8, 0) }; }
    void AddColumn(string name, string text, int width) {
        records.Columns.Add(new DataGridViewTextBoxColumn { Name = name, HeaderText = text, Width = width, MinimumWidth = width, AutoSizeMode = DataGridViewAutoSizeColumnMode.Fill, FillWeight = width });
    }
    string SelectedId() { var choice = botFilter.SelectedItem as BotChoice; return choice == null ? "" : choice.Id; }
    public void RefreshBotChoices() {
        string current = SelectedId(); updating = true;
        botFilter.Items.Clear(); botFilter.Items.Add(new BotChoice { Id = "", Label = "全部 Bot" });
        foreach (var bot in store.Value.Bots) botFilter.Items.Add(new BotChoice { Id = bot.Id, Label = bot.Name });
        if (archived.Checked) foreach (var bot in store.Value.ArchivedBots) botFilter.Items.Add(new BotChoice { Id = bot.Id, Label = bot.Name + "（已移除）" });
        botFilter.SelectedIndex = 0;
        for (int i = 0; i < botFilter.Items.Count; i++) if (((BotChoice)botFilter.Items[i]).Id == current) botFilter.SelectedIndex = i;
        updating = false;
    }
    public void SelectBot(string id) {
        RefreshBotChoices(); updating = true; botFilter.SelectedIndex = 0;
        for (int i = 0; i < botFilter.Items.Count; i++) if (((BotChoice)botFilter.Items[i]).Id == id) botFilter.SelectedIndex = i;
        updating = false;
        if (Visible && !preview) RefreshRecords();
    }
    BotProfile[] Profiles() {
        string id = SelectedId(); IEnumerable<BotProfile> bots = store.Value.Bots;
        if (archived.Checked) bots = bots.Concat(store.Value.ArchivedBots);
        return bots.Where(b => string.IsNullOrEmpty(id) || b.Id == id).Select(b => new BotProfile { Id = b.Id, Name = b.Name, Root = b.Root }).ToArray();
    }
    async void RefreshRecords() {
        if (IsDisposed || preview) return;
        if (loading) { refreshPending = true; return; }
        loading = true; refresh.Enabled = false; refresh.Text = "读取中"; var bots = Profiles();
        try {
            var result = await Task.Run(() => reader.Read(bots));
            if (!IsDisposed && !refreshPending) { snapshot = result; Render(); }
        } catch (Exception) {
            if (!IsDisposed) { rateNote.Text = "统计暂时无法刷新。已显示的数据保持不变，请稍后点击刷新。"; rateNote.ForeColor = Color.FromArgb(154, 96, 26); }
        } finally {
            loading = false;
            if (!IsDisposed) { refresh.Enabled = true; refresh.Text = "刷新记录"; }
        }
        if (refreshPending && !IsDisposed) { refreshPending = false; RefreshRecords(); }
    }
    static string Number(decimal? value) { return value.HasValue ? value.Value.ToString("N0", CultureInfo.InvariantCulture) : "—"; }
    static string Money(decimal? value, string prefix, int digits) { return value.HasValue ? prefix + value.Value.ToString("F" + digits, CultureInfo.InvariantCulture) : "—"; }
    void SetCard(MetricCard card, string value, string note) { card.ValueText = value; card.Note = note; card.Invalidate(); tips.SetToolTip(card, card.Caption + "：" + value + "\n" + note); }
    void Render() {
        if (snapshot == null || IsDisposed) return;
        int[] periods = { 1, 7, 30, 0 }; var totals = UsageTotals.From(snapshot, periods[Math.Max(0, periodFilter.SelectedIndex)], DateTime.Now);
        bool incomplete = snapshot.UnavailableBots > 0;
        bool noData = incomplete && totals.Entries.Count == 0;
        string scope = Convert.ToString(periodFilter.SelectedItem);
        SetCard(requests, noData ? "—" : Number(totals.Entries.Count), "筛选范围内 · " + snapshot.Bots + " 个 Bot");
        SetCard(total, noData || (totals.UnknownTokens > 0 && totals.Total == 0) ? "—" : Number(totals.Total), totals.UnknownTokens > 0 ? "部分 Token 字段缺失" : "输入 + 输出；含缓存");
        SetCard(input, noData ? "—" : Number(totals.Input), "缓存 " + Number(totals.Cached) + " Token");
        SetCard(output, noData ? "—" : Number(totals.Output), "平均耗时 " + (totals.AverageSeconds.HasValue ? totals.AverageSeconds.Value.ToString("F1") + " 秒" : "—"));
        SetCard(pictures, noData ? "—" : Number(totals.Images), "已进入答题请求的图片");
        decimal? cny = snapshot.Fx == null || noData || (totals.UnknownCost > 0 && totals.CostUsd == 0) ? (decimal?)null : totals.CostUsd * snapshot.Fx.Rate;
        SetCard(cost, Money(cny, "¥", 4), (noData ? "—" : Money(totals.CostUsd, "$", 6)) + (totals.UnknownCost > 0 ? " · 部分缺失" : " · 非实际账单"));
        string fx = snapshot.Fx == null ? "暂无有效汇率，人民币暂不换算。" : "缓存汇率 USD/CNY " + snapshot.Fx.Rate.ToString("F4") + " · 获取于 " + snapshot.Fx.FetchedAt.LocalDateTime.ToString("MM-dd HH:mm") + (DateTimeOffset.Now - snapshot.Fx.FetchedAt > TimeSpan.FromHours(2) ? "（较旧）" : "") + (snapshot.Fx.RefreshFailed ? " · 最近更新失败" : "");
        string accuracy = totals.Estimated > 0 ? totals.Estimated + " 条记录为文字估算或来源未标明。" : "Token 来自已记录的 Codex 用量。";
        var warnings = new List<string>(snapshot.Warnings);
        if (totals.UnknownTokens > 0) warnings.Add(totals.UnknownTokens + " 条记录存在缺失字段，汇总仅包含已知值");
        if (totals.UnknownCost > 0) warnings.Add(totals.UnknownCost + " 条记录费用未知，费用汇总不完整");
        string note = fx + "\n" + (warnings.Count > 0 ? string.Join("；", warnings) : accuracy + " 使用既有单价配置估算；非订阅账单。运行中的 Bot 每小时尝试更新汇率。");
        rateNote.Text = note; rateNote.ForeColor = warnings.Count > 0 ? Color.FromArgb(145, 94, 27) : UiTheme.Muted; tips.SetToolTip(rateNote, note + "\n" + accuracy);
        records.SuspendLayout(); records.Rows.Clear();
        foreach (var row in totals.Entries.Take(1000)) {
            int i = records.Rows.Add(row.At.LocalDateTime.ToString("MM-dd HH:mm:ss"), row.BotName, row.Model.Replace("gpt-5.6-", ""), Number(row.Input), Number(row.Cached), Number(row.Output), Number(row.Total), Number(row.Images), row.ElapsedMs.HasValue ? (row.ElapsedMs.Value / 1000M).ToString("F1") : "—", Money(snapshot.Fx != null && row.CostUsd.HasValue ? row.CostUsd.Value * snapshot.Fx.Rate : (decimal?)null, "¥", 4), Money(row.CostUsd, "$", 6), row.Source == "codex-reported" ? "Codex 上报" : row.Source == "estimated" ? "文字估算" : "未知来源");
            records.Rows[i].Cells[0].ToolTipText = row.At.LocalDateTime.ToString("yyyy-MM-dd HH:mm:ss"); records.Rows[i].Cells[2].ToolTipText = row.Model;
        }
        records.ResumeLayout();
        empty.Visible = totals.Entries.Count == 0;
        empty.Text = incomplete ? "统计暂时无法读取\n请检查上方提示，稍后刷新" : snapshot.Bots == 0 ? "还没有 Bot\n到「Bot 管理」新增一个账号" : "这个范围内还没有用量记录\n完成答题后会自动显示，也可以调整时间范围";
        if (empty.Visible) empty.BringToFront();
        footer.Text = "已读取 " + snapshot.ReadAt.ToString("HH:mm:ss") + " · 本页每 15 秒刷新 · 共 " + totals.Entries.Count + " 条" + (totals.Entries.Count > 1000 ? "，明细仅显示最近 1,000 条，汇总包含全部筛选记录" : "") + "\n每个 Bot 最多保留最近 1,000 条；这里不是无限期累计账单。";
    }
    public void PreparePreview() { periodFilter.SelectedIndex = 3; snapshot = reader.Read(Profiles()); Render(); }
    protected override void Dispose(bool disposing) { if (disposing) { timer.Dispose(); tips.Dispose(); } base.Dispose(disposing); }
}
}
