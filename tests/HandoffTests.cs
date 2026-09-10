using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Net;
using System.Net.Sockets;
using System.Threading.Tasks;

namespace WechatBotDesktop {
public static class HandoffTests {
    static void Check(bool value, string name, List<string> passed) { if (!value) throw new Exception("Handoff test failed: " + name); passed.Add(name); }
    static int FreeControlPort() { var listener = new TcpListener(IPAddress.Loopback, 0); listener.Start(); int port = ((IPEndPoint)listener.LocalEndpoint).Port; listener.Stop(); return port; }
    static BotProfile Fixture(string root, string id) {
        var bot = new BotProfile { Id = id, Name = id, Root = Path.Combine(root, id), Workspace = root, Port = FreeControlPort() - 1, Enabled = true, Managed = true };
        Directory.CreateDirectory(bot.StateDir); return bot;
    }
    static async Task LaunchFake(Engine engine, BotProfile bot, LaunchAttempt attempt) {
        string config = Path.Combine(bot.Root, "fixture-launch.json");
        Files.AtomicWrite(config, Files.Json.Serialize(new { id = bot.Id, launchId = attempt.Id, stateDir = bot.StateDir, workspace = bot.Workspace, port = bot.Port + 1, entry = Path.Combine(engine.DirectoryPath, "source", "tests", "fake-child.mjs"), env = new Dictionary<string, string>() }));
        attempt.Process = Process.Start(new ProcessStartInfo(engine.Node, Files.Quote(Path.Combine(engine.DirectoryPath, "controller.mjs")) + " " + Files.Quote(config)) { UseShellExecute = false, CreateNoWindow = true, WindowStyle = ProcessWindowStyle.Hidden, WorkingDirectory = bot.Root });
        for (int i = 0; i < 30; i++) { if ((await engine.Status(bot)).Managed) return; await Task.Delay(100); }
        throw new Exception("Local fixture controller did not become ready.");
    }
    public static async Task<List<string>> Run(string root, Engine engine) {
        var passed = new List<string>();
        Check(!Files.AppendDiagnostic(root, "cannot append to a directory"), "completed operation is not failed by diagnostic-log IO", passed);
        var identity = new Dictionary<string, object> { { "id", "a" }, { "launchId", "attempt" }, { "pid", 123 } };
        Check(Engine.MatchesLaunch(identity, "a", "attempt", 123) && !Engine.MatchesLaunch(identity, "b", "attempt", 123) && !Engine.MatchesLaunch(identity, "a", "other", 123) && !Engine.MatchesLaunch(identity, "a", "attempt", 124), "cleanup requires Bot ID, launch ID and exact PID", passed);
        var a = Fixture(root, "handoff-fixture-a"); var b = Fixture(root, "handoff-fixture-b");
        string operation = Guid.NewGuid().ToString("N"); a.HandoffPending = operation;
        using (var denied = new LaunchAttempt()) {
            bool blocked = false;
            try { await engine.StartOwned(a, new Settings(), denied); } catch (Exception) { blocked = true; }
            Check(blocked && denied.Process == null, "pending handoff blocks a new launch before process creation", passed);
        }
        string journal = Path.Combine(a.StateDir, "handoff-backups", operation, "operation.json");
        Files.AtomicWrite(journal, Files.Json.Serialize(new { Schema = 2, OperationId = operation, Project = a.Root, Phase = "stopped", Tasks = new object[0] }));
        Engine.MarkHandoff(a, operation, "committed");
        Check(Engine.HandoffPhase(a, operation) == "committed", "successful handoff gets an explicit commit marker", passed);
        Engine.MarkRecoveryNeeded(a, operation);
        Check(Engine.HandoffPhase(a, operation) == "restore-needed", "uncertain recovery remains visibly pending", passed);
        using (var first = new LaunchAttempt()) using (var second = new LaunchAttempt()) {
            Exception failure = null;
            try {
                await LaunchFake(engine, a, first); await LaunchFake(engine, b, second);
                await engine.CancelLaunch(a, first);
                Check(first.Process.HasExited && (await engine.Status(b)).Managed, "failed-start cleanup stops only this attempt and leaves other Bot alive", passed);
            } catch (Exception error) { failure = error; }
            try { await engine.CancelLaunch(a, first); } catch (Exception error) { failure = failure ?? error; }
            try { await engine.CancelLaunch(b, second); } catch (Exception error) { failure = failure ?? error; }
            if (failure != null) throw failure;
        }
        return passed;
    }
}
}
