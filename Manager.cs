using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Net;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using System.Reflection;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Web.Script.Serialization;
using System.Windows.Forms;
using Microsoft.Win32;

namespace WechatBotDesktop {
public class BotProfile {
    public string Id { get; set; }
    public string Name { get; set; }
    public string Root { get; set; }
    public string Workspace { get; set; }
    public int Port { get; set; }
    public bool AutoStart { get; set; }
    public bool Managed { get; set; }
    public bool Enabled { get; set; }
    public string LegacyProject { get; set; }
    public string HandoffPending { get; set; }
    public string StateDir { get { return Path.Combine(Root, ".runtime"); } }
}
public class Settings {
    public int Schema { get; set; }
    public int MaximumRunning { get; set; }
    public string CodexPath { get; set; }
    public string SkillPath { get; set; }
    public List<BotProfile> Bots { get; set; }
    public List<BotProfile> ArchivedBots { get; set; }
    public Settings() { Schema = 1; MaximumRunning = 3; Bots = new List<BotProfile>(); ArchivedBots = new List<BotProfile>(); }
}
public static class Files {
    public static readonly JavaScriptSerializer Json = new JavaScriptSerializer { MaxJsonLength = 20 * 1024 * 1024 };
    public static void AtomicWrite(string file, string content) {
        Directory.CreateDirectory(Path.GetDirectoryName(file));
        string temp = file + "." + Guid.NewGuid().ToString("N") + ".tmp";
        File.WriteAllText(temp, content, new UTF8Encoding(false));
        if (File.Exists(file)) File.Replace(temp, file, file + ".previous", true);
        else File.Move(temp, file);
    }
    public static string Quote(string text) {
        // CommandLineToArgvW-compatible quoting; user names never become shell text.
        var b = new StringBuilder("\""); int slashes = 0;
        foreach (char c in text) {
            if (c == '\\') { slashes++; continue; }
            if (c == '"') { b.Append('\\', slashes * 2 + 1); b.Append('"'); slashes = 0; continue; }
            b.Append('\\', slashes); slashes = 0; b.Append(c);
        }
        b.Append('\\', slashes * 2); return b.Append('"').ToString();
    }
    public static Dictionary<string, object> Object(string text) { return Json.Deserialize<Dictionary<string, object>>(text); }
    public static bool AppendDiagnostic(string path, string message) { try { File.AppendAllText(path, message, Encoding.UTF8); return true; } catch { return false; } }
    public static string Text(Dictionary<string, object> data, string key, string fallback) {
        object value; return data != null && data.TryGetValue(key, out value) && value != null ? Convert.ToString(value) : fallback;
    }
    public static string Tail(string path) {
        if (!File.Exists(path)) return "暂无日志。";
        using (var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete)) {
            bool skipFirst = stream.Length > 32000;
            stream.Seek(Math.Max(0, stream.Length - 32000), SeekOrigin.Begin);
            using (var reader = new StreamReader(stream, Encoding.UTF8, true)) {
                if (skipFirst) reader.ReadLine();
                return reader.ReadToEnd();
            }
        }
    }
}
public class Store {
    public string DataRoot;
    public Settings Value;
    public Store(string root, bool discover) {
        DataRoot = Path.GetFullPath(root); Directory.CreateDirectory(DataRoot);
        string file = Path.Combine(DataRoot, "manager.json");
        if (File.Exists(file)) {
            try { Value = Files.Json.Deserialize<Settings>(File.ReadAllText(file)); }
            catch { throw new Exception("管理配置无法读取。原文件仍保留在：" + file + "；上次备份为 manager.json.previous。"); }
            if (Value == null || Value.Bots == null || Value.ArchivedBots == null || Value.Schema != 1) throw new Exception("管理配置版本或结构不受支持。请保留原文件后恢复备份。");
        } else {
            Value = new Settings();
            if (discover) Discover();
            Save();
        }
        Value.MaximumRunning = Math.Max(1, Math.Min(20, Value.MaximumRunning));
        if (Value.Bots.GroupBy(b => b.Port).Any(g => g.Count() > 1) || Value.Bots.GroupBy(b => b.Id).Any(g => g.Count() > 1)) throw new Exception("配置中有重复 Bot 编号或端口。");
    }
    void Discover() {
        string legacy = @"D:\Codex\visualizations\2026\09\07\01a07cca-7520-7771-859e-903c5866ca9c\wechaty-codex-build";
        string second = @"D:\bot\gaokao-english-bot-2";
        if (Directory.Exists(Path.Combine(legacy, ".runtime"))) Value.Bots.Add(new BotProfile { Id = "bot-1", Name = "英语 Bot 1", Root = legacy, Workspace = Path.Combine(DataRoot, "workspaces", "bot-1"), Port = 3210, Enabled = true, AutoStart = false, Managed = false, LegacyProject = legacy });
        if (Directory.Exists(Path.Combine(second, ".runtime"))) Value.Bots.Add(new BotProfile { Id = "bot-2", Name = "英语 Bot 2", Root = second, Workspace = Path.Combine(DataRoot, "workspaces", "bot-2"), Port = 3220, Enabled = true, AutoStart = false, Managed = false, LegacyProject = second });
        if (Value.Bots.Count == 0) Add("英语 Bot 1");
    }
    public BotProfile Add(string name) {
        if (string.IsNullOrWhiteSpace(name) || name.Trim().Length > 40) throw new Exception("名称请填写 1–40 个字符。");
        var usedPorts = new HashSet<int>(Value.Bots.Concat(Value.ArchivedBots).Select(b => b.Port));
        var listening = new HashSet<int>(IPGlobalProperties.GetIPGlobalProperties().GetActiveTcpListeners().Select(p => p.Port));
        int port = 3230;
        while (usedPorts.Contains(port) || listening.Contains(port) || listening.Contains(port + 1)) port += 10;
        if (port > 65000) throw new Exception("没有可分配的本机端口。");
        string id = "bot-" + Guid.NewGuid().ToString("N").Substring(0, 10);
        var bot = new BotProfile { Id = id, Name = name.Trim(), Port = port, Root = Path.Combine(DataRoot, "bots", id), Workspace = Path.Combine(DataRoot, "workspaces", id), Enabled = true, Managed = true, AutoStart = false };
        Directory.CreateDirectory(bot.StateDir); Directory.CreateDirectory(bot.Workspace);
        Value.Bots.Add(bot); Save(); return bot;
    }
    public void Archive(BotProfile bot) { Value.Bots.Remove(bot); Value.ArchivedBots.Add(bot); bot.AutoStart = false; Save(); }
    public void Restore(BotProfile bot) { Value.ArchivedBots.Remove(bot); Value.Bots.Add(bot); Save(); }
    public void Save() { Files.AtomicWrite(Path.Combine(DataRoot, "manager.json"), Files.Json.Serialize(Value)); }
}
public class BotStatus {
    public bool Running, Managed, HasQr;
    public string Phase = "stopped", Message = "已停止";
    public Dictionary<string, object> Connection;
}
public sealed class LaunchAttempt : IDisposable {
    public readonly string Id = Guid.NewGuid().ToString("N");
    public string OperationId;
    public Process Process;
    public void Dispose() { if (Process != null) Process.Dispose(); }
}
public class Engine {
    public string DirectoryPath;
    public string Node { get { return Path.Combine(DirectoryPath, "node.exe"); } }
    public Engine(string cacheRoot) {
        var assembly = Assembly.GetExecutingAssembly();
        string hash;
        using (Stream payload = assembly.GetManifestResourceStream("engine.zip")) {
            if (payload == null) throw new Exception("程序包缺少运行资源。");
            using (var sha = SHA256.Create()) hash = BitConverter.ToString(sha.ComputeHash(payload)).Replace("-", "").Substring(0, 16);
        }
        DirectoryPath = Path.Combine(cacheRoot, "engine-" + hash);
        string complete = Path.Combine(DirectoryPath, ".complete");
        if (File.Exists(complete) && File.Exists(Node) && File.Exists(Path.Combine(DirectoryPath, "bot.mjs"))) return;
        string staging = DirectoryPath + ".staging-" + Guid.NewGuid().ToString("N");
        Directory.CreateDirectory(staging);
        using (Stream payload = assembly.GetManifestResourceStream("engine.zip"))
        using (var archive = new ZipArchive(payload, ZipArchiveMode.Read)) {
            foreach (var entry in archive.Entries) {
                string target = Path.GetFullPath(Path.Combine(staging, entry.FullName.Replace('/', Path.DirectorySeparatorChar)));
                if (!target.StartsWith(staging + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase)) throw new Exception("程序包存在无效路径。");
                if (entry.FullName.EndsWith("/")) { Directory.CreateDirectory(target); continue; }
                Directory.CreateDirectory(Path.GetDirectoryName(target));
                using (var input = entry.Open()) using (var output = File.Create(target)) input.CopyTo(output);
            }
        }
        File.WriteAllText(Path.Combine(staging, ".complete"), hash);
        if (Directory.Exists(DirectoryPath)) Directory.Move(DirectoryPath, DirectoryPath + ".incomplete-" + DateTime.Now.ToString("yyyyMMddHHmmss"));
        Directory.Move(staging, DirectoryPath);
    }
    public string ResolveCodex(Settings settings) {
        if (!string.IsNullOrWhiteSpace(settings.CodexPath) && File.Exists(settings.CodexPath)) return settings.CodexPath;
        string local = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        var candidates = new List<string> {
            Path.Combine(local, @"Programs\OpenAI\Codex\bin\codex.exe"),
            Path.Combine(local, @"OpenAI\Codex\bin\codex.exe"),
        };
        foreach (string part in (Environment.GetEnvironmentVariable("PATH") ?? "").Split(';')) {
            try { candidates.Add(Path.Combine(part.Trim('"'), "codex.exe")); } catch { }
        }
        return candidates.FirstOrDefault(File.Exists);
    }
    public string ResolveSkill(Settings settings) {
        if (!string.IsNullOrWhiteSpace(settings.SkillPath) && File.Exists(settings.SkillPath)) return settings.SkillPath;
        string profile = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        string[] candidates = { @"D:\Codex\skills\gaokao-english\SKILL.md", Path.Combine(profile, @".skills-manager\skills\gaokao-english\SKILL.md"), Path.Combine(profile, @".codex\skills\gaokao-english\SKILL.md"), Path.Combine(DirectoryPath, @"skills\gaokao-english\SKILL.md") };
        return candidates.FirstOrDefault(File.Exists);
    }
    public static async Task<string> Request(int port, string route, string method, string token, string body) {
        var request = (HttpWebRequest)WebRequest.Create("http://127.0.0.1:" + port + route);
        request.Proxy = null; request.Method = method; request.Timeout = 1200; request.ReadWriteTimeout = 1200; request.KeepAlive = false;
        if (token != null) request.Headers[HttpRequestHeader.Authorization] = "Bearer " + token;
        var cancel = new CancellationTokenSource(1500);
        using (cancel.Token.Register(request.Abort)) {
            if (method == "POST") {
                byte[] bytes = Encoding.UTF8.GetBytes(body ?? ""); request.ContentType = "text/plain"; request.ContentLength = bytes.Length;
                using (var stream = await request.GetRequestStreamAsync()) await stream.WriteAsync(bytes, 0, bytes.Length);
            }
            using (var response = await request.GetResponseAsync()) using (var reader = new StreamReader(response.GetResponseStream())) return await reader.ReadToEndAsync();
        }
    }
    static string ReadToken(BotProfile bot) {
        var control = Files.Object(File.ReadAllText(Path.Combine(bot.StateDir, "desktop-control.json")));
        if (Files.Text(control, "id", "") != bot.Id || Convert.ToInt32(control["port"]) != bot.Port + 1) throw new Exception("进程与 Bot 配置不匹配。");
        return Files.Text(control, "token", "");
    }
    public async Task<BotStatus> Status(BotProfile bot) {
        var status = new BotStatus();
        try {
            string token = ReadToken(bot);
            var value = Files.Object(await Request(bot.Port + 1, "/status", "GET", token, null));
            if (Files.Text(value, "kind", "") == "wechat-bot-desktop" && Files.Text(value, "id", "") == bot.Id) {
                status.Running = true; status.Managed = true;
                object connection; if (value.TryGetValue("connection", out connection)) status.Connection = connection as Dictionary<string, object>;
                status.Phase = Files.Text(status.Connection, "phase", "starting"); status.Message = Files.Text(status.Connection, "message", "正在启动");
            }
        } catch { }
        if (!status.Running) {
            try {
                var health = Files.Object(await Request(bot.Port, "/health", "GET", null, null));
                if (Files.Text(health, "ok", "").Equals("True", StringComparison.OrdinalIgnoreCase)) { status.Running = true; status.Phase = "legacy"; status.Message = "旧版运行中，可点击接管"; }
            } catch { }
        }
        string qr = Path.Combine(bot.StateDir, "wechat-login-qr.png");
        status.HasQr = status.Running && (status.Phase == "awaiting-scan" || status.Phase == "awaiting-confirmation" || status.Phase == "awaiting-pair") && File.Exists(qr);
        if (!bot.Enabled && !status.Running) status.Message = "已停用";
        return status;
    }
    public async Task Start(BotProfile bot, Settings settings) {
        using (var attempt = new LaunchAttempt()) {
            Exception failure = null;
            try { await StartOwned(bot, settings, attempt); } catch (Exception error) { failure = error; }
            if (failure == null) return;
            await CancelLaunch(bot, attempt);
            throw failure;
        }
    }
    public async Task StartOwned(BotProfile bot, Settings settings, LaunchAttempt attempt) {
        if (!string.IsNullOrEmpty(bot.HandoffPending) && bot.HandoffPending != attempt.OperationId) throw new Exception("上次接管尚未确认完成，已暂停此 Bot 的重复启动。请检查 handoff-actions.log 和本次备份。");
        var status = await Status(bot);
        if (status.Running) { if (status.Managed) return; throw new Exception("该 Bot 正由旧版运行，请点击“接管旧版”。"); }
        if (!bot.Enabled) throw new Exception("请先启用该 Bot。");
        string codex = ResolveCodex(settings); if (codex == null) throw new Exception("没有找到本机 Codex。请安装并登录 Codex，然后点击“选择 Codex”。");
        string skill = ResolveSkill(settings); if (skill == null) throw new Exception("没有找到 gaokao-english 答题规则。");
        Directory.CreateDirectory(bot.StateDir); Directory.CreateDirectory(bot.Workspace);
        string config = Path.Combine(bot.Root, "desktop-launch.json");
        var env = new Dictionary<string, string> {
            { "CODEX_BIN", codex }, { "CODEX_WORKSPACE", bot.Workspace }, { "CODEX_MODEL", "gpt-5.6-luna" },
            { "CODEX_REASONING_EFFORT", "medium" }, { "CODEX_SANDBOX", "read-only" },
            { "BOT_SKILL_PATH", skill }, { "WEIXIN_BASE_URL", "https://ilinkai.weixin.qq.com" },
            { "WECHAT_ALLOW_GROUPS", "false" }, { "USAGE_PORT", bot.Port.ToString() },
        };
        Files.AtomicWrite(config, Files.Json.Serialize(new { id = bot.Id, launchId = attempt.Id, stateDir = bot.StateDir, workspace = bot.Workspace, port = bot.Port + 1, entry = Path.Combine(DirectoryPath, "bot.mjs"), env = env }));
        var start = new ProcessStartInfo(Node, Files.Quote(Path.Combine(DirectoryPath, "controller.mjs")) + " " + Files.Quote(config));
        start.UseShellExecute = false; start.CreateNoWindow = true; start.WindowStyle = ProcessWindowStyle.Hidden; start.WorkingDirectory = bot.Root;
        attempt.Process = Process.Start(start);
        var process = attempt.Process;
        {
            for (int i = 0; i < 20; i++) {
                await Task.Delay(250);
                if (process.HasExited) throw new Exception("启动失败，可能存在端口或数据目录冲突。请查看运行日志。");
                if ((await Status(bot)).Managed) { bot.Managed = true; return; }
            }
        }
        throw new Exception("启动尚未就绪，请查看日志后重试。");
    }
    public static bool MatchesLaunch(Dictionary<string, object> value, string botId, string launchId, int pid) {
        return Files.Text(value, "id", "") == botId && Files.Text(value, "launchId", "") == launchId && Files.Text(value, "pid", "") == pid.ToString();
    }
    public async Task CancelLaunch(BotProfile bot, LaunchAttempt attempt) {
        if (attempt.Process == null) return;
        bool stopSent = false;
        for (int i = 0; i < 24; i++) {
            if (attempt.Process.HasExited && PortsAvailable(bot)) return;
            if (!stopSent) {
                try {
                    var control = Files.Object(File.ReadAllText(Path.Combine(bot.StateDir, "desktop-control.json")));
                    if (MatchesLaunch(control, bot.Id, attempt.Id, attempt.Process.Id)) {
                        string token = Files.Text(control, "token", "");
                        var state = Files.Object(await Request(bot.Port + 1, "/status", "GET", token, null));
                        if (MatchesLaunch(state, bot.Id, attempt.Id, attempt.Process.Id)) { await Request(bot.Port + 1, "/stop", "POST", token, ""); stopSent = true; }
                    }
                } catch { /* Wait for this exact controller; never target another live instance. */ }
            }
            await Task.Delay(250);
        }
        throw new Exception("无法确认本次启动的进程已停止；为避免新旧实例冲突，未自动恢复旧启动任务。请检查运行日志和 handoff-backups 中的本次备份。");
    }
    static bool PortsAvailable(BotProfile bot) {
        try { return !IPGlobalProperties.GetIPGlobalProperties().GetActiveTcpListeners().Any(p => p.Port == bot.Port || p.Port == bot.Port + 1); } catch { return false; }
    }
    public async Task Stop(BotProfile bot) {
        var status = await Status(bot); if (!status.Running) return;
        if (!status.Managed) throw new Exception("该 Bot 由旧版进程运行，请先点击“接管旧版”。");
        await Request(bot.Port + 1, "/stop", "POST", ReadToken(bot), "");
        for (int i = 0; i < 16; i++) { await Task.Delay(250); if (!(await Status(bot)).Running) return; }
        throw new Exception("进程仍在结束当前任务，请稍后重试。");
    }
    public async Task Relogin(BotProfile bot, Settings settings) {
        await Stop(bot);
        string backup = Path.Combine(bot.StateDir, "login-backups", DateTime.Now.ToString("yyyyMMdd-HHmmss") + "-" + Guid.NewGuid().ToString("N").Substring(0, 6));
        foreach (string name in new[] { "ilink-account.json", "ilink-sync.txt", "threads.json", "pending-batches.json", "wechat-login-qr.png", "pairing-code.txt" }) {
            string original = Path.Combine(bot.StateDir, name);
            if (File.Exists(original)) { Directory.CreateDirectory(backup); File.Move(original, Path.Combine(backup, name)); }
        }
        await Start(bot, settings);
    }
    public static void ValidateHandoff(BotProfile bot) {
        if (string.IsNullOrEmpty(bot.LegacyProject) || !Directory.Exists(bot.LegacyProject)) throw new Exception("找不到旧版目录。");
        string root = Path.GetFullPath(bot.Root).TrimEnd(Path.DirectorySeparatorChar), legacy = Path.GetFullPath(bot.LegacyProject).TrimEnd(Path.DirectorySeparatorChar);
        if (!root.Equals(legacy, StringComparison.OrdinalIgnoreCase) || root.Equals(Path.GetPathRoot(root).TrimEnd(Path.DirectorySeparatorChar), StringComparison.OrdinalIgnoreCase)) throw new Exception("旧版项目与 Bot 数据目录不一致，接管已中止。");
    }
    public static string HandoffPhase(BotProfile bot, string operationId) {
        string file = Path.Combine(bot.StateDir, "handoff-backups", operationId, "operation.json");
        Dictionary<string, object> journal;
        try { journal = Files.Object(File.ReadAllText(file)); } catch (FileNotFoundException) { return "missing"; } catch (DirectoryNotFoundException) { return "missing"; }
        if (Files.Text(journal, "OperationId", "") != operationId || !Path.GetFullPath(Files.Text(journal, "Project", "")).TrimEnd(Path.DirectorySeparatorChar).Equals(Path.GetFullPath(bot.Root).TrimEnd(Path.DirectorySeparatorChar), StringComparison.OrdinalIgnoreCase)) throw new Exception("接管记录与选中的 Bot 不匹配。");
        return Files.Text(journal, "Phase", "unknown");
    }
    public static void MarkHandoff(BotProfile bot, string operationId, string phase) {
        if (HandoffPhase(bot, operationId) == "missing") throw new Exception("缺少本次接管记录。");
        string file = Path.Combine(bot.StateDir, "handoff-backups", operationId, "operation.json");
        var journal = Files.Object(File.ReadAllText(file)); journal["Phase"] = phase;
        Files.AtomicWrite(file, Files.Json.Serialize(journal)); Files.AtomicWrite(Path.Combine(bot.StateDir, "desktop-handoff.json"), Files.Json.Serialize(journal));
    }
    public static void MarkRecoveryNeeded(BotProfile bot, string operationId) { try { MarkHandoff(bot, operationId, "restore-needed"); } catch { /* Pending flag and existing journal still block a new launch. */ } }
    public async Task Handoff(BotProfile bot, bool restore, string operationId) {
        ValidateHandoff(bot);
        string script = Path.Combine(DirectoryPath, "legacy-handoff.ps1");
        string powershell = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.System), @"WindowsPowerShell\v1.0\powershell.exe");
        var start = new ProcessStartInfo(powershell, "-NoProfile -NonInteractive -ExecutionPolicy Bypass -File " + Files.Quote(script) + " -ProjectPath " + Files.Quote(bot.LegacyProject) + " -OperationId " + Files.Quote(operationId) + (restore ? " -Restore" : ""));
        start.UseShellExecute = false; start.CreateNoWindow = true; start.WindowStyle = ProcessWindowStyle.Hidden; start.RedirectStandardError = true; start.RedirectStandardOutput = true;
        start.StandardErrorEncoding = Encoding.UTF8; start.StandardOutputEncoding = Encoding.UTF8;
        string handoffLog = Path.Combine(bot.StateDir, "handoff-actions.log"); Directory.CreateDirectory(bot.StateDir);
        File.AppendAllText(handoffLog, DateTime.UtcNow.ToString("o") + " " + operationId + (restore ? " restore started" : " handoff started") + Environment.NewLine, Encoding.UTF8);
        using (var process = Process.Start(start)) {
            var stderr = process.StandardError.ReadToEndAsync(); var stdout = process.StandardOutput.ReadToEndAsync();
            // Keep the operation busy until its script really exits. A timeout must not leave
            // a task-changing script running behind an apparent failure or allow a second handoff.
            await Task.Run(() => process.WaitForExit());
            string error = await stderr; string output = await stdout;
            Files.AppendDiagnostic(handoffLog, DateTime.UtcNow.ToString("o") + " " + operationId + " exit=" + process.ExitCode + Environment.NewLine + output + error + Environment.NewLine);
            if (process.ExitCode != 0) throw new Exception("旧版接管未完成：" + error);
        }
        bot.Managed = !restore;
    }
}

public class MainForm : Form {
    Store store; Engine engine; bool busy, polling, exiting; bool preview;
    DataGridView grid = new DataGridView(); Label counts = new Label(), detail = new Label(), qrHint = new Label();
    PictureBox qr = new PictureBox(); TextBox logs = new TextBox(), pair = new TextBox();
    NumericUpDown maximum = new NumericUpDown(); CheckBox auto = new CheckBox(), startup = new CheckBox();
    NotifyIcon tray; System.Windows.Forms.Timer timer = new System.Windows.Forms.Timer();
    Dictionary<string, BotStatus> statuses = new Dictionary<string, BotStatus>();
    HashSet<string> desired = new HashSet<string>(); Dictionary<string, DateTime> attempts = new Dictionary<string, DateTime>();
    Dictionary<string, int> missing = new Dictionary<string, int>();
    DateTime qrStamp = DateTime.MinValue; string qrBot = ""; bool rebuilding;
    Control managementPage; UsagePanel usagePage; UiActionButton manageNav, usageNav;
    public MainForm(Store store, Engine engine, bool preview) {
        this.store = store; this.engine = engine; this.preview = preview;
        SuspendLayout(); AutoScaleDimensions = new SizeF(96, 96); AutoScaleMode = AutoScaleMode.Dpi;
        Text = "微信英语 Bot 管理器"; Size = new Size(1280, 880); MinimumSize = new Size(1140, 800); StartPosition = FormStartPosition.CenterScreen;
        Font = new Font("Microsoft YaHei UI", 10F); BackColor = UiTheme.Canvas; ForeColor = UiTheme.Ink; AutoScaleMode = AutoScaleMode.Dpi;
        Icon = System.Drawing.Icon.ExtractAssociatedIcon(Application.ExecutablePath) ?? SystemIcons.Application;
        var shell = new TableLayoutPanel { Dock = DockStyle.Fill, RowCount = 4, ColumnCount = 1, Padding = new Padding(24, 18, 24, 10) };
        shell.RowStyles.Add(new RowStyle(SizeType.Absolute, 74)); shell.RowStyles.Add(new RowStyle(SizeType.Absolute, 50)); shell.RowStyles.Add(new RowStyle(SizeType.Percent, 100)); shell.RowStyles.Add(new RowStyle(SizeType.Absolute, 28)); Controls.Add(shell);
        var header = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 2, RowCount = 1, Margin = new Padding(0) };
        header.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        header.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 56)); header.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 44));
        var brand = new Panel { Dock = DockStyle.Fill, Margin = new Padding(0) };
        brand.Controls.Add(new PictureBox { Image = Icon.ToBitmap(), SizeMode = PictureBoxSizeMode.Zoom, Location = new Point(0, 7), Size = new Size(48, 48) });
        brand.Controls.Add(new Label { Text = "微信英语 Bot", Font = new Font(Font.FontFamily, 23, FontStyle.Bold), AutoSize = true, ForeColor = UiTheme.Ink, Location = new Point(60, 0) });
        brand.Controls.Add(new Label { Text = "WECHAT BOT DESKTOP  /  账号 · 答题 · 用量", Font = new Font(Font.FontFamily, 8.5F), AutoSize = true, ForeColor = UiTheme.Muted, Location = new Point(63, 42) });
        counts.Name = "counts"; counts.Dock = DockStyle.Fill; counts.TextAlign = ContentAlignment.MiddleRight; counts.ForeColor = UiTheme.Green; counts.Font = new Font(Font.FontFamily, 11, FontStyle.Bold);
        header.Controls.Add(brand); header.Controls.Add(counts); shell.Controls.Add(header, 0, 0);
        var nav = new FlowLayoutPanel { Dock = DockStyle.Fill, WrapContents = false, Margin = new Padding(0) };
        manageNav = new UiActionButton("Bot 管理", "bots", true); usageNav = new UiActionButton("用量统计", "chart", false);
        manageNav.Click += (s, e) => ShowPage(false, null); usageNav.Click += (s, e) => ShowPage(true, null);
        nav.Controls.Add(manageNav); nav.Controls.Add(usageNav); nav.Controls.Add(new Label { Text = "独立账号 · 本地数据 · 自动恢复", AutoSize = true, ForeColor = UiTheme.Muted, Font = new Font(Font.FontFamily, 9F), Margin = new Padding(14, 9, 0, 0) }); shell.Controls.Add(nav, 0, 1);
        var pages = new Panel { Dock = DockStyle.Fill, Margin = new Padding(0) }; shell.Controls.Add(pages, 0, 2);
        var layout = new TableLayoutPanel { Dock = DockStyle.Fill, RowCount = 3, ColumnCount = 1, Margin = new Padding(0) };
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 54)); layout.RowStyles.Add(new RowStyle(SizeType.Percent, 100)); layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 108)); managementPage = layout; pages.Controls.Add(layout);
        usagePage = new UsagePanel(store, preview) { Visible = false }; pages.Controls.Add(usagePage);
        var toolbar = new FlowLayoutPanel { Dock = DockStyle.Fill, WrapContents = false, Margin = new Padding(0) };
        toolbar.Controls.Add(Button("＋ 新增 Bot", () => Run(AddBot), true)); toolbar.Controls.Add(Button("批量启动", () => Run(StartAll), false)); toolbar.Controls.Add(Button("批量停止", () => Run(StopAll), false));
        toolbar.Controls.Add(new Label { Text = "同时运行上限", AutoSize = true, Margin = new Padding(18, 10, 5, 0) });
        maximum.Name = "maximum"; maximum.Minimum = 1; maximum.Maximum = 20; maximum.Value = store.Value.MaximumRunning; maximum.Width = 58; maximum.Margin = new Padding(0); maximum.Dock = DockStyle.Top;
        maximum.ValueChanged += (s, e) => { store.Value.MaximumRunning = (int)maximum.Value; store.Save(); };
        var capacityControl = new Panel { Width = 68, Height = 38, Padding = new Padding(0, 5, 0, 0), Margin = new Padding(0, 0, 8, 0) }; capacityControl.Controls.Add(maximum); toolbar.Controls.Add(capacityControl);
        startup.Text = "登录 Windows 后启动"; startup.AutoSize = true; startup.Margin = new Padding(15, 9, 0, 0); startup.Checked = IsStartupEnabled();
        startup.Click += (s, e) => { try { SetStartup(startup.Checked); } catch (Exception error) { startup.Checked = IsStartupEnabled(); Error(error); } }; toolbar.Controls.Add(startup);
        layout.Controls.Add(toolbar, 0, 0);
        var middle = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 2, RowCount = 1, Margin = new Padding(0) };
        middle.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 62)); middle.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 38)); layout.Controls.Add(middle, 0, 1);
        grid.Dock = DockStyle.Fill; grid.BackgroundColor = Color.White; grid.BorderStyle = BorderStyle.None; grid.AutoGenerateColumns = false; grid.AllowUserToAddRows = false; grid.AllowUserToDeleteRows = false; grid.ReadOnly = true;
        grid.SelectionMode = DataGridViewSelectionMode.FullRowSelect; grid.MultiSelect = false; grid.RowHeadersVisible = false; grid.EnableHeadersVisualStyles = false;
        UiTheme.StyleGrid(grid); grid.AllowUserToResizeRows = false; grid.RowTemplate.Height = 50;
        AddColumn("name", "名称", 30); AddColumn("state", "状态", 34); AddColumn("port", "用量端口", 17); AddColumn("auto", "自动启动", 19);
        grid.SelectionChanged += (s, e) => { if (!rebuilding) RefreshDetail(); }; grid.CellPainting += PaintStatus;
        var listCard = new TableLayoutPanel { Dock = DockStyle.Fill, RowCount = 2, ColumnCount = 1, Margin = new Padding(0), BackColor = Color.White, Padding = new Padding(12, 0, 12, 12) };
        listCard.RowStyles.Add(new RowStyle(SizeType.Absolute, 44)); listCard.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        listCard.Controls.Add(new Label { Text = "账号列表", AutoSize = true, Font = new Font(Font.FontFamily, 11, FontStyle.Bold), Margin = new Padding(0, 11, 0, 0) }, 0, 0); grid.Margin = new Padding(0); listCard.Controls.Add(grid, 0, 1); middle.Controls.Add(listCard, 0, 0);
        var tabs = new TabControl { Dock = DockStyle.Fill, Margin = new Padding(16, 0, 0, 0), DrawMode = TabDrawMode.OwnerDrawFixed, SizeMode = TabSizeMode.Fixed, ItemSize = new Size(142, 36) }; middle.Controls.Add(tabs, 1, 0);
        tabs.DrawItem += (s, e) => { using (var fill = new SolidBrush(e.Index == tabs.SelectedIndex ? Color.White : UiTheme.Canvas)) e.Graphics.FillRectangle(fill, e.Bounds); UiIcons.Draw(e.Graphics, e.Index == 0 ? "qr" : "code", new Rectangle(e.Bounds.X + 12, e.Bounds.Y + 8, 18, 18), UiTheme.Green); TextRenderer.DrawText(e.Graphics, tabs.TabPages[e.Index].Text, Font, new Rectangle(e.Bounds.X + 36, e.Bounds.Y, e.Bounds.Width - 36, e.Bounds.Height), UiTheme.Ink, TextFormatFlags.VerticalCenter | TextFormatFlags.Left); };
        var login = new TabPage("扫码与状态") { BackColor = Color.White }; var logTab = new TabPage("运行日志") { BackColor = Color.White }; tabs.TabPages.Add(login); tabs.TabPages.Add(logTab);
        var loginLayout = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 5, Padding = new Padding(12) };
        loginLayout.RowStyles.Add(new RowStyle(SizeType.Absolute, 60)); loginLayout.RowStyles.Add(new RowStyle(SizeType.Percent, 100)); loginLayout.RowStyles.Add(new RowStyle(SizeType.Absolute, 64)); loginLayout.RowStyles.Add(new RowStyle(SizeType.Absolute, 48)); loginLayout.RowStyles.Add(new RowStyle(SizeType.Absolute, 30)); login.Controls.Add(loginLayout);
        detail.Dock = DockStyle.Fill; detail.ForeColor = Color.FromArgb(40, 76, 55); loginLayout.Controls.Add(detail, 0, 0);
        qr.Dock = DockStyle.Fill; qr.SizeMode = PictureBoxSizeMode.Zoom; qr.BackColor = Color.White; loginLayout.Controls.Add(qr, 0, 1);
        qr.Paint += (s, e) => { if (qr.Image == null) { int edge = Math.Min(76, Math.Min(qr.Width, qr.Height)); if (edge > 0) UiIcons.Draw(e.Graphics, "qr", new Rectangle((qr.Width - edge) / 2, (qr.Height - edge) / 2, edge, edge), Color.FromArgb(200, 220, 209)); } };
        qrHint.Dock = DockStyle.Fill; qrHint.TextAlign = ContentAlignment.MiddleCenter; qrHint.ForeColor = Color.DimGray; loginLayout.Controls.Add(qrHint, 0, 2);
        var pairLine = new FlowLayoutPanel { Dock = DockStyle.Fill, WrapContents = false }; pair.Width = 140; pair.MaxLength = 12; pair.Margin = new Padding(0, 4, 8, 0); pairLine.Controls.Add(pair); pairLine.Controls.Add(Button("提交配对数字", () => Run(SubmitPair), false)); loginLayout.Controls.Add(pairLine, 0, 3);
        auto.AutoSize = true; auto.Text = "随管理器自动启动此 Bot"; auto.Click += (s, e) => { var b = Selected(); if (b != null) { b.AutoStart = auto.Checked; store.Save(); Rebuild(b.Id); } }; loginLayout.Controls.Add(auto, 0, 4);
        logs.Multiline = true; logs.ScrollBars = ScrollBars.Both; logs.ReadOnly = true; logs.WordWrap = false; logs.Dock = DockStyle.Fill; logs.Font = new Font("Consolas", 9F); logs.BackColor = Color.White; logTab.Controls.Add(logs);
        tabs.SelectedIndexChanged += (s, e) => RefreshDetail();
        var actions = new FlowLayoutPanel { Dock = DockStyle.Fill, Padding = new Padding(0, 12, 0, 0), Margin = new Padding(0) };
        actions.Controls.Add(Button("启动", () => Run(StartSelected), true)); actions.Controls.Add(Button("停止", () => Run(StopSelected), false));
        actions.Controls.Add(Button("重新扫码", () => Run(Relink), false)); actions.Controls.Add(Button("查看用量", OpenUsage, false)); actions.Controls.Add(Button("接管旧版", () => Run(TakeOver), false));
        actions.Controls.Add(Button("改名", Rename, false)); actions.Controls.Add(Button("启用 / 停用", () => Run(Toggle), false)); actions.Controls.Add(Button("移除", () => Run(Archive), false)); actions.Controls.Add(Button("恢复已移除", Restore, false));
        actions.Controls.Add(Button("数据目录", OpenData, false)); actions.Controls.Add(Button("选择 Codex", SelectCodex, false)); actions.Controls.Add(Button("答题规则", OpenSkill, false)); actions.Controls.Add(Button("完整源码", () => Open(Path.Combine(engine.DirectoryPath, "source")), false));
        layout.Controls.Add(actions, 0, 2);
        shell.Controls.Add(new Label { Text = "Luna · 中等  |  多图传完发送 /回答  |  /新题 清除上下文  |  /追问 接着问  |  关闭窗口后在托盘继续运行", AutoSize = true, Font = new Font(Font.FontFamily, 8.5F), ForeColor = UiTheme.Muted, Margin = new Padding(0, 7, 0, 0) }, 0, 3);
        if (!preview) {
            tray = new NotifyIcon { Icon = Icon, Text = "微信英语 Bot 管理器", Visible = true };
            var menu = new ContextMenuStrip(); menu.Items.Add("打开管理器", UiIcons.Bitmap("bots"), (s, e) => ShowWindow()); menu.Items.Add("停止全部并退出", UiIcons.Bitmap("stop"), (s, e) => Run(ExitApp)); tray.ContextMenuStrip = menu; tray.DoubleClick += (s, e) => ShowWindow();
            timer.Interval = 3000; timer.Tick += async (s, e) => await Poll();
            Shown += async (s, e) => { await Poll(); foreach (var b in store.Value.Bots.Where(b => b.AutoStart && b.Enabled && b.Managed)) desired.Add(b.Id); timer.Start(); await AutoResume(); };
            FormClosing += (s, e) => { if (!exiting && e.CloseReason == CloseReason.UserClosing) { e.Cancel = true; Hide(); } else { timer.Stop(); if (tray != null) tray.Dispose(); } };
        }
        Rebuild(null);
        ResumeLayout(false);
    }
    Button Button(string text, Action click, bool primary) {
        var b = new UiActionButton(text, UiTheme.ButtonIcon(text), primary);
        b.Click += (s, e) => { if (!busy) { try { click(); } catch (Exception error) { Error(error); } } }; return b;
    }
    void ShowPage(bool usage, string botId) {
        if (usage && botId != null) usagePage.SelectBot(botId);
        manageNav.Primary = !usage; usageNav.Primary = usage; managementPage.Visible = !usage; usagePage.Visible = usage;
        if (usage) usagePage.BringToFront(); else managementPage.BringToFront();
    }
    void PaintStatus(object sender, DataGridViewCellPaintingEventArgs e) {
        if (e.RowIndex < 0 || e.ColumnIndex != 1) return;
        e.PaintBackground(e.ClipBounds, true); string text = Convert.ToString(e.FormattedValue);
        bool running = text == "已连接" || text == "正在答题" || text == "旧版运行";
        bool caution = text.Contains("待") || text.Contains("失败") || text.Contains("过期") || text.Contains("恢复");
        Color ink = running ? UiTheme.Green : caution ? Color.FromArgb(159, 103, 26) : UiTheme.Muted;
        Color fill = running ? Color.FromArgb(225, 243, 232) : caution ? Color.FromArgb(252, 243, 222) : Color.FromArgb(237, 241, 239);
        int width = Math.Min(e.CellBounds.Width - 16, TextRenderer.MeasureText(text, e.CellStyle.Font).Width + 23);
        if (width > 20) {
            var bounds = new Rectangle(e.CellBounds.X + 8, e.CellBounds.Y + (e.CellBounds.Height - 28) / 2, width, 28);
            using (var shape = UiTheme.Rounded(bounds, 8)) using (var brush = new SolidBrush(fill)) e.Graphics.FillPath(brush, shape);
            TextRenderer.DrawText(e.Graphics, text, e.CellStyle.Font, bounds, ink, TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis);
        }
        e.Handled = true;
    }
    void AddColumn(string key, string caption, float weight) { grid.Columns.Add(new DataGridViewTextBoxColumn { Name = key, HeaderText = caption, AutoSizeMode = DataGridViewAutoSizeColumnMode.Fill, FillWeight = weight }); }
    BotProfile Selected() { return grid.CurrentRow == null ? null : grid.CurrentRow.Tag as BotProfile; }
    BotProfile Require() { var bot = Selected(); if (bot == null) throw new Exception("请先选择或新增一个 Bot。"); return bot; }
    void Error(Exception error) { MessageBox.Show(this, error.Message, "微信 Bot", MessageBoxButtons.OK, MessageBoxIcon.Information); }
    async void Run(Func<Task> action) { if (busy) return; busy = true; UseWaitCursor = true; try { await action(); } catch (Exception error) { Error(error); } finally { busy = false; UseWaitCursor = false; } await Poll(); }
    void Rebuild(string selected) {
        if (selected == null && Selected() != null) selected = Selected().Id;
        rebuilding = true; grid.Rows.Clear();
        foreach (var bot in store.Value.Bots) {
            BotStatus state; statuses.TryGetValue(bot.Id, out state); state = state ?? new BotStatus();
            string label = ShortPhase(state, bot);
            int i = grid.Rows.Add(bot.Name, label, bot.Port, bot.AutoStart ? "是" : "否"); grid.Rows[i].Tag = bot;
            if (state.Phase == "online") grid.Rows[i].Cells[1].Style.ForeColor = Color.FromArgb(26, 119, 68);
            if (bot.Id == selected) grid.CurrentCell = grid.Rows[i].Cells[0];
        }
        rebuilding = false; UpdateCounts(); RefreshDetail(); if (usagePage != null) usagePage.RefreshBotChoices();
    }
    static string ShortPhase(BotStatus status, BotProfile bot) {
        if (!string.IsNullOrEmpty(bot.HandoffPending)) return "接管待确认";
        if (!bot.Enabled && !status.Running) return "已停用";
        switch (status.Phase) { case "online": return "已连接"; case "working": return "正在答题"; case "awaiting-scan": return "待扫码"; case "awaiting-confirmation": return "待手机确认"; case "awaiting-pair": return "待配对数字"; case "legacy": return "旧版运行"; case "login-expired": return "登录已过期"; case "reconnecting": case "restarting": return "自动恢复中"; case "reply-error": return "回复失败"; case "connecting": case "starting": return "启动中"; default: return "已停止"; }
    }
    void UpdateCounts() {
        var live = store.Value.Bots.Select(b => statuses.ContainsKey(b.Id) ? statuses[b.Id] : new BotStatus()).ToList();
        counts.Text = "总数 " + store.Value.Bots.Count + "    运行中 " + live.Count(s => s.Running) + "    待扫码 " + live.Count(s => s.Phase.StartsWith("awaiting"));
    }
    async Task Poll() {
        if (polling || preview || IsDisposed) return; polling = true;
        try {
            var bots = store.Value.Bots.ToArray();
            var results = await Task.WhenAll(bots.Select(b => engine.Status(b)));
            for (int i = 0; i < bots.Length; i++) statuses[bots[i].Id] = results[i];
            foreach (DataGridViewRow row in grid.Rows) { var b = row.Tag as BotProfile; if (b != null && statuses.ContainsKey(b.Id)) row.Cells[1].Value = ShortPhase(statuses[b.Id], b); }
            UpdateCounts(); RefreshDetail();
        } finally { polling = false; }
        if (!busy) await AutoResume();
    }
    async Task AutoResume() {
        if (busy || preview) return;
        foreach (var bot in store.Value.Bots.Where(b => b.Enabled && b.Managed && string.IsNullOrEmpty(b.HandoffPending) && desired.Contains(b.Id)).ToArray()) {
            BotStatus s; statuses.TryGetValue(bot.Id, out s);
            if (s != null && s.Running) { missing[bot.Id] = 0; continue; }
            int count; missing.TryGetValue(bot.Id, out count); missing[bot.Id] = count + 1;
            DateTime last; attempts.TryGetValue(bot.Id, out last);
            if (count < 1 || DateTime.UtcNow - last < TimeSpan.FromSeconds(30)) continue;
            attempts[bot.Id] = DateTime.UtcNow;
            busy = true;
            try { await CheckCapacity(bot); await engine.Start(bot, store.Value); store.Save(); }
            catch (Exception error) { logs.Text = "自动启动：" + error.Message + Environment.NewLine + logs.Text; }
            finally { busy = false; }
        }
    }
    void RefreshDetail() {
        var bot = Selected(); if (bot == null) { detail.Text = "点击“新增 Bot”创建账号"; qrHint.Text = "每个 Bot 独立扫码登录"; ClearQr(); return; }
        BotStatus state; statuses.TryGetValue(bot.Id, out state); state = state ?? new BotStatus();
        detail.Text = bot.Name + "\n" + state.Message; auto.Checked = bot.AutoStart;
        if (state.HasQr) {
            qrHint.Text = "使用此 Bot 的微信账号扫码，并在手机确认。";
            string path = Path.Combine(bot.StateDir, "wechat-login-qr.png");
            try { var stamp = File.GetLastWriteTimeUtc(path); if (stamp != qrStamp || qrBot != bot.Id) {
                using (var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete)) using (var image = Image.FromStream(stream)) { ClearQr(); qr.Image = new Bitmap(image); }
                qrStamp = stamp; qrBot = bot.Id;
            } } catch { /* PNG may still be being refreshed. */ }
        } else { ClearQr(); qrHint.Text = state.Running ? (state.Phase == "legacy" ? "旧版运行中，可先查看用量，或点击接管。" : "已登录时无需重复扫码。\n换微信账号请点击“重新扫码”。") : "点击“启动”，获取新二维码或恢复已有登录。"; }
        try { logs.Text = "[接管记录]\r\n" + Files.Tail(Path.Combine(bot.StateDir, "handoff-actions.log")) + "\r\n[进程状态]\r\n" + Files.Tail(Path.Combine(bot.StateDir, "supervisor.log")) + "\r\n[最近消息]\r\n" + Files.Tail(Path.Combine(bot.StateDir, "bridge.stdout.log")) + "\r\n[错误记录]\r\n" + Files.Tail(Path.Combine(bot.StateDir, "bridge.stderr.log")); } catch { }
    }
    void ClearQr() { if (qr.Image != null) { qr.Image.Dispose(); qr.Image = null; } qrStamp = DateTime.MinValue; qrBot = ""; }
    async Task CheckCapacity(BotProfile target) {
        var all = await Task.WhenAll(store.Value.Bots.Where(b => b.Id != target.Id).Select(engine.Status));
        if (all.Count(s => s.Running) >= store.Value.MaximumRunning) throw new Exception("已达到同时运行上限（" + store.Value.MaximumRunning + "）。请先停止其他 Bot，或调整上限。");
    }
    async Task StartBot(BotProfile bot) { if (!bot.Enabled) throw new Exception("请先启用该 Bot。"); if (!(await engine.Status(bot)).Running) await CheckCapacity(bot); await engine.Start(bot, store.Value); desired.Add(bot.Id); attempts[bot.Id] = DateTime.UtcNow; store.Save(); }
    Task StartSelected() { return StartBot(Require()); }
    async Task StopSelected() { var bot = Require(); desired.Remove(bot.Id); await engine.Stop(bot); }
    Task AddBot() { string name = AskText("新增 Bot", "给这个 Bot 起个名字", "英语 Bot " + (store.Value.Bots.Count + 1)); if (name != null) { var bot = store.Add(name); Rebuild(bot.Id); } return Task.FromResult(0); }
    async Task StartAll() {
        var errors = new List<string>();
        foreach (var bot in store.Value.Bots.Where(b => b.Enabled)) {
            if ((await engine.Status(bot)).Running) continue;
            try { await StartBot(bot); } catch (Exception error) { errors.Add(bot.Name + "：" + error.Message); }
        }
        if (errors.Count > 0) throw new Exception(string.Join("\n", errors));
    }
    async Task StopAll() {
        desired.Clear(); var errors = new List<string>();
        foreach (var bot in store.Value.Bots) try { await engine.Stop(bot); } catch (Exception error) { errors.Add(bot.Name + "：" + error.Message); }
        if (errors.Count > 0) throw new Exception(string.Join("\n", errors));
    }
    async Task Relink() {
        var bot = Require();
        if (MessageBox.Show(this, "重新扫码将结束该 Bot 当前任务，并把原登录与上下文存为本地备份。继续？", "重新扫码", MessageBoxButtons.OKCancel) != DialogResult.OK) return;
        desired.Remove(bot.Id); await CheckCapacity(bot); await engine.Relogin(bot, store.Value); desired.Add(bot.Id); store.Save();
    }
    async Task SubmitPair() { var bot = Require(); string token = Files.Text(Files.Object(File.ReadAllText(Path.Combine(bot.StateDir, "desktop-control.json"))), "token", ""); await Engine.Request(bot.Port + 1, "/pair", "POST", token, pair.Text.Trim()); pair.Clear(); }
    async Task TakeOver() {
        var bot = Require(); var status = await engine.Status(bot); if (status.Managed) return;
        if (!string.IsNullOrEmpty(bot.HandoffPending)) throw new Exception("此 Bot 有未完成的接管操作。请先检查接管日志和备份；当前不会重复执行接管。");
        if (MessageBox.Show(this, "接管会停止该 Bot 的旧进程和旧自启动，再由此程序运行。原微信登录和用量记录保留。继续？", "接管旧版", MessageBoxButtons.OKCancel) != DialogResult.OK) return;
        if (engine.ResolveCodex(store.Value) == null || engine.ResolveSkill(store.Value) == null) throw new Exception("请先安装登录 Codex 并确认答题规则存在。");
        Engine.ValidateHandoff(bot);
        await CheckCapacity(bot);
        desired.Remove(bot.Id); string operationId = Guid.NewGuid().ToString("N");
        bot.HandoffPending = operationId;
        try { store.Save(); } catch { bot.HandoffPending = null; throw; }
        Exception handoffError = null;
        try { await engine.Handoff(bot, false, operationId); } catch (Exception error) { handoffError = error; }
        if (handoffError != null) {
            string phase = Engine.HandoffPhase(bot, operationId);
            if (phase == "missing" || phase == "restored") { bot.HandoffPending = null; store.Save(); }
            throw handoffError;
        }
        using (var attempt = new LaunchAttempt { OperationId = operationId }) {
            Exception startError = null;
            try { await engine.StartOwned(bot, store.Value, attempt); store.Save(); Engine.MarkHandoff(bot, operationId, "committed"); bot.HandoffPending = null; store.Save(); } catch (Exception error) { startError = error; }
            if (startError != null) {
                bot.HandoffPending = operationId;
                Exception cleanupError = null;
                try { await engine.CancelLaunch(bot, attempt); } catch (Exception error) { cleanupError = error; }
                if (cleanupError != null) { Engine.MarkRecoveryNeeded(bot, operationId); throw cleanupError; }
                try { await engine.Handoff(bot, true, operationId); bot.HandoffPending = null; store.Save(); }
                catch (Exception restoreError) { bot.HandoffPending = operationId; Engine.MarkRecoveryNeeded(bot, operationId); throw new Exception(startError.Message + "\n旧启动设置恢复未完成：" + restoreError.Message); }
                throw new Exception(startError.Message + "\n已按本次备份恢复旧计划任务设置；没有计划任务的手动进程请手动启动。");
            }
        }
        desired.Add(bot.Id); attempts[bot.Id] = DateTime.UtcNow;
    }
    void Rename() { var bot = Require(); string name = AskText("修改名称", "Bot 名称", bot.Name); if (name == null) return; if (string.IsNullOrWhiteSpace(name) || name.Length > 40) throw new Exception("名称请填写 1–40 个字符。"); bot.Name = name.Trim(); store.Save(); Rebuild(bot.Id); }
    async Task Toggle() { var bot = Require(); if (bot.Enabled) { desired.Remove(bot.Id); await engine.Stop(bot); bot.Enabled = false; } else bot.Enabled = true; store.Save(); Rebuild(bot.Id); }
    async Task Archive() {
        var bot = Require(); if (MessageBox.Show(this, "从列表移除“" + bot.Name + "”？账号与记录仍保留，可通过“恢复已移除”找回。", "移除 Bot", MessageBoxButtons.OKCancel) != DialogResult.OK) return;
        desired.Remove(bot.Id); await engine.Stop(bot); store.Archive(bot); Rebuild(null);
    }
    void Restore() {
        if (store.Value.ArchivedBots.Count == 0) throw new Exception("没有已移除的 Bot。");
        using (var dialog = new Form { Text = "恢复已移除的 Bot", Size = new Size(390, 300), StartPosition = FormStartPosition.CenterParent, Font = Font, MinimizeBox = false, MaximizeBox = false }) {
            dialog.SuspendLayout(); dialog.AutoScaleDimensions = new SizeF(96, 96); dialog.AutoScaleMode = AutoScaleMode.Dpi;
            var list = new ListBox { Dock = DockStyle.Fill, DisplayMember = "Name" }; foreach (var b in store.Value.ArchivedBots) list.Items.Add(b);
            var ok = new UiActionButton("恢复", "restore", true) { Dock = DockStyle.Bottom, Height = 40, DialogResult = DialogResult.OK }; dialog.Controls.Add(list); dialog.Controls.Add(ok);
            dialog.ResumeLayout(false); if (dialog.ShowDialog(this) == DialogResult.OK && list.SelectedItem != null) { var bot = (BotProfile)list.SelectedItem; store.Restore(bot); Rebuild(bot.Id); }
        }
    }
    void SelectCodex() { using (var dialog = new OpenFileDialog { Title = "选择已登录的 Codex 程序", Filter = "Codex 程序|codex.exe" }) if (dialog.ShowDialog(this) == DialogResult.OK) { store.Value.CodexPath = dialog.FileName; store.Save(); } }
    void OpenData() { var bot = Require(); Directory.CreateDirectory(bot.Root); Open(bot.Root); }
    void OpenUsage() { ShowPage(true, Require().Id); }
    void OpenSkill() { string path = engine.ResolveSkill(store.Value); if (path == null) throw new Exception("答题规则不存在。"); Open(Path.GetDirectoryName(path)); }
    static void Open(string path) { Process.Start(new ProcessStartInfo(path) { UseShellExecute = true }); }
    void ShowWindow() { Show(); WindowState = FormWindowState.Normal; Activate(); }
    async Task ExitApp() { await StopAll(); exiting = true; if (tray != null) tray.Dispose(); Close(); }
    const string StartupValue = "WechatEnglishBotManager";
    bool IsStartupEnabled() { using (var key = Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Run", false)) return key != null && key.GetValue(StartupValue) != null; }
    void SetStartup(bool enabled) {
        using (var key = Registry.CurrentUser.CreateSubKey(@"Software\Microsoft\Windows\CurrentVersion\Run")) {
            if (enabled) key.SetValue(StartupValue, Files.Quote(Application.ExecutablePath) + " --tray"); else key.DeleteValue(StartupValue, false);
        }
    }
    public void StartMinimized() { Shown += (s, e) => BeginInvoke(new Action(Hide)); }
    public void SavePreview(string target) {
        ShowInTaskbar = false; StartPosition = FormStartPosition.Manual; Location = new Point(-15000, -15000);
        Show(); Application.DoEvents(); PerformLayout();
        CheckVisibleLayout(this);
        var elements = new List<object>(); CaptureLayout(this, elements); Files.AtomicWrite(Path.ChangeExtension(target, ".layout.json"), Files.Json.Serialize(elements));
        using (var bitmap = new Bitmap(Width, Height)) { DrawToBitmap(bitmap, new Rectangle(Point.Empty, Size)); bitmap.Save(target, System.Drawing.Imaging.ImageFormat.Png); }
        Hide();
    }
    static void CheckVisibleLayout(Control parent) {
        foreach (Control child in parent.Controls) {
            if (child.Visible && (child is UiActionButton || child.Name == "maximum") && !parent.ClientRectangle.Contains(child.Bounds)) throw new Exception("界面控件超出可见区域：" + child.Text);
            CheckVisibleLayout(child);
        }
    }
    static void CaptureLayout(Control parent, List<object> elements) {
        foreach (Control child in parent.Controls) { elements.Add(new { type = child.GetType().Name, name = child.Name, text = child is TextBox ? "" : child.Text, visible = child.Visible, bounds = child.Bounds.ToString(), client = child.ClientSize.ToString(), parent = parent.GetType().Name, parentBounds = parent.ClientRectangle.ToString() }); CaptureLayout(child, elements); }
    }
    public void SaveUsagePreview(string target) { ShowPage(true, ""); usagePage.PreparePreview(); SavePreview(target); }
    public void CheckButtonIcons() { CheckButtons(this); }
    static void CheckButtons(Control parent) {
        foreach (Control control in parent.Controls) {
            var button = control as Button;
            if (button != null && (!(button is UiActionButton) || string.IsNullOrEmpty(((UiActionButton)button).IconKey))) throw new Exception("按钮缺少图标：" + button.Text);
            CheckButtons(control);
        }
    }
    protected override void Dispose(bool disposing) { if (disposing) { timer.Dispose(); if (tray != null) tray.Dispose(); ClearQr(); } base.Dispose(disposing); }
    string AskText(string title, string label, string value) {
        using (var dialog = new Form { Text = title, Size = new Size(420, 180), FormBorderStyle = FormBorderStyle.FixedDialog, StartPosition = FormStartPosition.CenterParent, MinimizeBox = false, MaximizeBox = false, Font = Font }) {
            dialog.SuspendLayout(); dialog.AutoScaleDimensions = new SizeF(96, 96); dialog.AutoScaleMode = AutoScaleMode.Dpi;
            var caption = new Label { Text = label, Location = new Point(16, 14), AutoSize = true }; var input = new TextBox { Text = value, Location = new Point(16, 43), Width = 370, MaxLength = 40 };
            var ok = new UiActionButton("确定", "check", true) { Location = new Point(198, 82), DialogResult = DialogResult.OK }; var cancel = new UiActionButton("取消", "close", false) { Location = new Point(296, 82), DialogResult = DialogResult.Cancel };
            dialog.Controls.AddRange(new Control[] { caption, input, ok, cancel }); dialog.AcceptButton = ok; dialog.CancelButton = cancel;
            dialog.ResumeLayout(false);
            return dialog.ShowDialog(this) == DialogResult.OK ? input.Text : null;
        }
    }
}
public static class Program {
    [STAThread] public static int Main(string[] args) {
        Application.EnableVisualStyles(); Application.SetCompatibleTextRenderingDefault(false);
        try {
            string basePath = Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location);
            bool selfTest = args.Length > 1 && args[0] == "--self-test";
            string root = selfTest ? Path.GetFullPath(args[1]) : Path.Combine(basePath, "data");
            if (args.Length > 1 && args[0] == "--prepare") root = Path.GetFullPath(args[1]);
            string mutexSuffix; using (var sha = SHA256.Create()) mutexSuffix = BitConverter.ToString(sha.ComputeHash(Encoding.UTF8.GetBytes(root.ToUpperInvariant()))).Replace("-", "").Substring(0, 16);
            bool created;
            using (var mutex = new Mutex(true, "Local\\WechatEnglishBot-" + mutexSuffix, out created)) {
                if (!created) { MessageBox.Show("管理器已在运行，请点击右下角托盘图标打开。", "微信 Bot"); return 0; }
                var engine = new Engine(Path.Combine(Path.GetDirectoryName(root), "程序文件")); var store = new Store(root, !selfTest);
                if (selfTest) {
                    var a = store.Add("隔离测试 A"); var b = store.Add("隔离测试 B");
                    if (a.Port == b.Port || a.Root == b.Root || a.Workspace == b.Workspace) throw new Exception("隔离测试失败");
                    File.WriteAllText(Path.Combine(a.StateDir, "test-sentinel.txt"), "preserve"); store.Archive(a);
                    if (!File.Exists(Path.Combine(a.StateDir, "test-sentinel.txt"))) throw new Exception("归档保留测试失败"); store.Restore(a);
                    var reloaded = new Store(root, false); if (reloaded.Value.Bots.Count != 2) throw new Exception("持久化测试失败");
                    var cases = new List<string> { "payload extraction", "unique ports", "isolated state and workspace", "archive preserves data", "restore", "configuration reload" };
                    cases.AddRange(UsageTests.Run(root, a, b));
                    cases.AddRange(Task.Run(() => HandoffTests.Run(root, engine)).GetAwaiter().GetResult());
                    using (var form = new MainForm(reloaded, engine, true)) { form.CheckButtonIcons(); form.SavePreview(Path.Combine(root, "ui-preview.png")); form.SaveUsagePreview(Path.Combine(root, "usage-preview.png")); form.Size = form.MinimumSize; form.SaveUsagePreview(Path.Combine(root, "usage-minimum-preview.png")); }
                    cases.Add("all action buttons have icons"); cases.Add("management and usage pages render");
                    Files.AtomicWrite(Path.Combine(root, "self-test.json"), Files.Json.Serialize(new { ok = true, cases = cases, codexFound = engine.ResolveCodex(store.Value) != null, skillFound = engine.ResolveSkill(store.Value) != null }));
                    return 0;
                }
                if (args.Length > 0 && args[0] == "--prepare") return 0;
                using (var form = new MainForm(store, engine, false)) { if (args.Contains("--tray")) form.StartMinimized(); Application.Run(form); }
                return 0;
            }
        } catch (Exception error) {
            try { File.WriteAllText(Path.Combine(Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location), "startup-error.txt"), error.ToString()); } catch { }
            if (!args.Contains("--self-test") && !args.Contains("--prepare")) MessageBox.Show(error.Message, "微信 Bot 启动失败");
            return 1;
        }
    }
}
}
