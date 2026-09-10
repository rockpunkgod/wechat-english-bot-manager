using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using System.Web.Script.Serialization;

namespace WechatBotDesktop {
public class UsageEntry {
    public DateTimeOffset At;
    public string Model, Source, BotName, BotId;
    public decimal? Input, Cached, Output, Total, Images, ElapsedMs, CostUsd;
    public UsageEntry ForBot(BotProfile bot) {
        var copy = (UsageEntry)MemberwiseClone(); copy.BotId = bot.Id; copy.BotName = bot.Name; return copy;
    }
}
public class UsageFx {
    public decimal Rate;
    public DateTimeOffset FetchedAt;
    public bool RefreshFailed;
}
public class UsageSnapshot {
    public List<UsageEntry> Entries = new List<UsageEntry>();
    public List<string> Warnings = new List<string>();
    public UsageFx Fx;
    public int Bots, EmptyBots, UnavailableBots;
    public DateTime ReadAt;
}
class UsageCache {
    public List<UsageEntry> Entries;
    public int InvalidRecords;
    public UsageFx Fx;
}
// Reads statistics only. No model calls, HTTP requests, writes, or login-file access.
public class UsageReader {
    readonly Dictionary<string, UsageCache> cache = new Dictionary<string, UsageCache>(StringComparer.OrdinalIgnoreCase);
    static decimal? Number(Dictionary<string, object> value, string key) {
        object raw; decimal result;
        if (!value.TryGetValue(key, out raw) || raw == null || raw is bool || raw is string) return null;
        if (!decimal.TryParse(Convert.ToString(raw, CultureInfo.InvariantCulture), NumberStyles.Float, CultureInfo.InvariantCulture, out result) || result < 0 || result > 1000000000000000M) return null;
        return result;
    }
    static decimal? Tokens(Dictionary<string, object> value, string key) {
        decimal? n = Number(value, key); return n.HasValue && decimal.Truncate(n.Value) == n.Value ? n : null;
    }
    static string Text(Dictionary<string, object> value, string key, string fallback) {
        object raw; return value.TryGetValue(key, out raw) && raw is string ? (string)raw : fallback;
    }
    static Dictionary<string, object> ReadJson(string file) {
        for (int attempt = 0; ; attempt++) {
            try {
                using (var stream = new FileStream(file, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete)) {
                    if (stream.Length > 8 * 1024 * 1024) throw new InvalidDataException("统计文件超过安全读取上限");
                    using (var reader = new StreamReader(stream, Encoding.UTF8, true)) {
                        var serializer = new JavaScriptSerializer { MaxJsonLength = 8 * 1024 * 1024 };
                        var result = serializer.DeserializeObject(reader.ReadToEnd()) as Dictionary<string, object>;
                        if (result == null) throw new InvalidDataException("统计文件结构不正确");
                        return result;
                    }
                }
            } catch (FileNotFoundException) { throw; }
            catch (DirectoryNotFoundException) { throw; }
            catch (Exception) { if (attempt > 0) throw; Thread.Sleep(70); }
        }
    }
    static List<UsageEntry> ParseRecords(Dictionary<string, object> value, out int invalid) {
        object raw; invalid = 0;
        if (!value.TryGetValue("records", out raw) || !(raw is IList)) throw new InvalidDataException("缺少用量记录列表");
        var result = new List<UsageEntry>();
        foreach (object item in (IList)raw) {
            var record = item as Dictionary<string, object>; DateTimeOffset at;
            if (record == null || !DateTimeOffset.TryParse(Text(record, "at", ""), CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal, out at)) { invalid++; continue; }
            var entry = new UsageEntry { At = at, Model = Text(record, "model", "未知模型"), Source = Text(record, "source", "unknown"), Input = Tokens(record, "inputTokens"), Cached = Tokens(record, "cachedInputTokens"), Output = Tokens(record, "outputTokens"), Total = Tokens(record, "totalTokens"), Images = Tokens(record, "images"), ElapsedMs = Number(record, "elapsedMs"), CostUsd = Number(record, "costUsd") };
            if (entry.Cached.HasValue && entry.Input.HasValue) entry.Cached = Math.Min(entry.Cached.Value, entry.Input.Value);
            // Reasoning is part of output; do not add it to the total a second time.
            if (!entry.Total.HasValue && entry.Input.HasValue && entry.Output.HasValue) entry.Total = entry.Input.Value + entry.Output.Value;
            if (!entry.CostUsd.HasValue && entry.Model == "gpt-5.6-luna" && entry.Input.HasValue && entry.Output.HasValue) {
                decimal cached = entry.Cached ?? 0;
                // Backward-compatible configured rates, not a claim about current API pricing.
                entry.CostUsd = ((entry.Input.Value - cached) * .20M + cached * .02M + entry.Output.Value * 1.20M) / 1000000M;
            }
            result.Add(entry);
        }
        return result;
    }
    public UsageSnapshot Read(IEnumerable<BotProfile> bots) {
        var snapshot = new UsageSnapshot { ReadAt = DateTime.Now };
        var directories = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var bot in bots) {
            string directory;
            try { directory = Path.GetFullPath(bot.StateDir).TrimEnd(Path.DirectorySeparatorChar); }
            catch { snapshot.UnavailableBots++; snapshot.Warnings.Add(bot.Name + "：数据目录无效"); continue; }
            if (!directories.Add(directory)) { snapshot.Warnings.Add(bot.Name + "：与其他 Bot 共用统计目录，已去重"); continue; }
            snapshot.Bots++;
            UsageCache previous; if (!cache.TryGetValue(directory, out previous)) { previous = new UsageCache(); cache[directory] = previous; }
            bool unavailable = false;
            try {
                int invalid; previous.Entries = ParseRecords(ReadJson(Path.Combine(directory, "usage.json")), out invalid); previous.InvalidRecords = invalid;
            } catch (FileNotFoundException) { unavailable = previous.Entries != null; }
            catch (DirectoryNotFoundException) { unavailable = previous.Entries != null; }
            catch (Exception) { unavailable = true; }
            if (unavailable) {
                snapshot.UnavailableBots++;
                snapshot.Warnings.Add(bot.Name + (previous.Entries != null ? "：文件暂时不可读，保留上次成功读取的记录" : "：统计读取失败，不代表用量为零"));
            }
            if (previous.Entries != null) {
                snapshot.Entries.AddRange(previous.Entries.Select(e => e.ForBot(bot)));
                if (previous.InvalidRecords > 0) snapshot.Warnings.Add(bot.Name + "：已跳过 " + previous.InvalidRecords + " 条无效记录");
                if (previous.Entries.Count == 0) snapshot.EmptyBots++;
            } else if (!unavailable) snapshot.EmptyBots++;
            try {
                var fxData = ReadJson(Path.Combine(directory, "usd-cny-rate.json")); DateTimeOffset fetchedAt;
                decimal? rate = Number(fxData, "rate");
                if (!rate.HasValue || rate.Value < 5 || rate.Value > 10 || !DateTimeOffset.TryParse(Text(fxData, "fetchedAt", ""), CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal, out fetchedAt) || fetchedAt > DateTimeOffset.Now.AddMinutes(5)) throw new InvalidDataException("缓存汇率无效");
                previous.Fx = new UsageFx { Rate = rate.Value, FetchedAt = fetchedAt, RefreshFailed = !string.IsNullOrEmpty(Text(fxData, "error", "")) };
            } catch (FileNotFoundException) { }
            catch (DirectoryNotFoundException) { }
            catch (Exception) { snapshot.Warnings.Add(bot.Name + "：汇率缓存暂时不可读"); }
            if (previous.Fx != null && (snapshot.Fx == null || previous.Fx.FetchedAt > snapshot.Fx.FetchedAt)) snapshot.Fx = previous.Fx;
        }
        return snapshot;
    }
}
public class UsageTotals {
    public List<UsageEntry> Entries;
    public decimal Input, Cached, Output, Total, Images, CostUsd;
    public int UnknownCost, UnknownTokens, Estimated;
    public decimal? AverageSeconds;
    public static UsageTotals From(UsageSnapshot snapshot, int days, DateTime now) {
        DateTime start = days == 0 ? DateTime.MinValue : now.Date.AddDays(1 - days);
        var rows = snapshot.Entries.Where(e => days == 0 || (e.At.LocalDateTime >= start && e.At.LocalDateTime < now.Date.AddDays(1))).OrderByDescending(e => e.At).ToList();
        var timed = rows.Where(e => e.ElapsedMs.HasValue).ToList();
        return new UsageTotals { Entries = rows, Input = rows.Sum(e => e.Input ?? 0), Cached = rows.Sum(e => e.Cached ?? 0), Output = rows.Sum(e => e.Output ?? 0), Total = rows.Sum(e => e.Total ?? 0), Images = rows.Sum(e => e.Images ?? 0), CostUsd = rows.Sum(e => e.CostUsd ?? 0), UnknownCost = rows.Count(e => !e.CostUsd.HasValue), UnknownTokens = rows.Count(e => !e.Total.HasValue || !e.Input.HasValue || !e.Output.HasValue), Estimated = rows.Count(e => e.Source != "codex-reported"), AverageSeconds = timed.Count == 0 ? (decimal?)null : timed.Average(e => e.ElapsedMs.Value) / 1000M };
    }
}
}
