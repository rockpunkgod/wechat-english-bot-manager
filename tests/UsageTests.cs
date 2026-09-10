using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace WechatBotDesktop {
public static class UsageTests {
    static void Check(bool value, string name, List<string> passed) { if (!value) throw new Exception("Usage test failed: " + name); passed.Add(name); }
    static Dictionary<string, object> Record(DateTimeOffset at, string model, decimal input, decimal cached, decimal output, decimal? cost, string source) {
        var row = new Dictionary<string, object> { { "at", at.ToString("o") }, { "model", model }, { "inputTokens", input }, { "cachedInputTokens", cached }, { "outputTokens", output }, { "reasoningTokens", 50 }, { "images", 2 }, { "elapsedMs", 14500 }, { "source", source } };
        if (cost.HasValue) row["costUsd"] = cost.Value; return row;
    }
    public static List<string> Run(string root, BotProfile a, BotProfile b) {
        var passed = new List<string>();
        var today = new DateTimeOffset(DateTime.Today.AddHours(12));
        var aRows = new[] {
            Record(today, "gpt-5.6-luna", 120000, 100000, 1200, .123456M, "codex-reported"),
            Record(today.AddDays(-2), "gpt-5.6-luna", 2000, 500, 300, null, "codex-reported"),
            Record(today.AddDays(-35), "gpt-5.6-luna", 1000, 0, 200, .00044M, "codex-reported")
        };
        aRows[0]["totalTokens"] = 121200;
        var bRows = new[] {
            Record(today.AddMinutes(-5), "other-model", 8000, 999999, 800, null, "codex-reported"),
            Record(today.AddDays(-1), "gpt-5.6-luna", 400, 0, 600, .0008M, "estimated")
        };
        string aFile = Path.Combine(a.StateDir, "usage.json");
        string original = Files.Json.Serialize(new { records = aRows });
        File.WriteAllText(aFile, original);
        File.WriteAllText(Path.Combine(b.StateDir, "usage.json"), Files.Json.Serialize(new { records = bRows }));
        File.WriteAllText(Path.Combine(a.StateDir, "usd-cny-rate.json"), Files.Json.Serialize(new { rate = 7.1, fetchedAt = DateTimeOffset.Now.AddHours(-3).ToString("o") }));
        File.WriteAllText(Path.Combine(b.StateDir, "usd-cny-rate.json"), Files.Json.Serialize(new { rate = 6.7085, fetchedAt = DateTimeOffset.Now.ToString("o") }));
        var reader = new UsageReader(); var all = reader.Read(new[] { a, b });
        Check(all.Entries.Count == 5 && all.Bots == 2, "all registered Bots aggregated", passed);
        Check(reader.Read(new[] { a }).Entries.Count == 3, "single Bot filter", passed);
        Check(UsageTotals.From(all, 1, today.LocalDateTime).Entries.Count == 2 && UsageTotals.From(all, 7, today.LocalDateTime).Entries.Count == 4 && UsageTotals.From(all, 30, today.LocalDateTime).Entries.Count == 4 && UsageTotals.From(all, 0, today.LocalDateTime).Entries.Count == 5, "local-date and saved-history filters", passed);
        Check(all.Entries.First(e => e.BotId == a.Id).CostUsd == .123456M, "stored dollar cost preserved", passed);
        var fallback = all.Entries.First(e => e.Input == 2000);
        Check(fallback.CostUsd == .00067M && fallback.Total == 2300, "cache-aware fallback without double-counting reasoning", passed);
        Check(all.Entries.First(e => e.Model == "other-model").CostUsd == null && UsageTotals.From(all, 0, today.LocalDateTime).UnknownCost == 1, "unknown model cost remains unknown", passed);
        Check(all.Entries.First(e => e.Model == "other-model").Cached == 8000 && UsageTotals.From(all, 0, today.LocalDateTime).Estimated == 1, "cached tokens clamped and estimates identified", passed);
        Check(all.Fx != null && all.Fx.Rate == 6.7085M, "latest valid cached exchange rate selected", passed);
        var duplicate = reader.Read(new[] { a, b, new BotProfile { Id = "same-directory", Name = "Duplicate", Root = a.Root } });
        Check(duplicate.Entries.Count == 5 && duplicate.Warnings.Count > 0, "shared statistics directory counted once", passed);
        File.WriteAllText(aFile, "{\"records\":["); var failed = reader.Read(new[] { a });
        Check(failed.Entries.Count == 3 && failed.UnavailableBots == 1 && failed.Warnings.Count > 0, "partial writes retain last good snapshot with warning", passed);
        File.WriteAllText(aFile, original);
        Check(reader.Read(new[] { a }).UnavailableBots == 0, "refresh recovers after transient parse error", passed);
        var empty = new BotProfile { Id = "empty", Name = "Empty", Root = Path.Combine(root, "usage-empty-fixture") };
        var noHistory = reader.Read(new[] { empty });
        Check(noHistory.Entries.Count == 0 && noHistory.UnavailableBots == 0 && noHistory.EmptyBots == 1 && noHistory.Fx == null, "missing history and FX distinguished from read failure", passed);
        Directory.CreateDirectory(empty.StateDir); File.WriteAllText(Path.Combine(empty.StateDir, "usage.json"), "invalid-json");
        Check(reader.Read(new[] { empty }).UnavailableBots == 1, "unreadable first snapshot is not zero usage", passed);
        File.WriteAllText(Path.Combine(empty.StateDir, "usage.json"), "{\"records\":[{}, {\"at\":\"" + today.ToString("o") + "\",\"inputTokens\":-1,\"outputTokens\":true,\"costUsd\":-5}]}");
        var invalid = reader.Read(new[] { empty });
        Check(invalid.Entries.Count == 1 && invalid.Warnings.Count == 1 && invalid.Entries[0].Input == null && invalid.Entries[0].Output == null && invalid.Entries[0].CostUsd == null, "invalid records and numeric fields are not trusted", passed);
        return passed;
    }
}
}
