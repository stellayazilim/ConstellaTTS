#!/usr/bin/env dotnet-script
// infra/test_ipc.csx — smoke test for the IPC layer
//
// Prerequisites:
//   1. `dotnet script infra/setup.csx` has been run (Python + msgpack installed)
//   2. The SDK.IPC project has been built:
//        dotnet build src/ConstellaTTS.SDK.IPC/ConstellaTTS.SDK.IPC.csproj
//
// Run with:
//   dotnet script infra/test_ipc.csx

#r "nuget: MessagePack, 3.1.4"
#r "../src/ConstellaTTS.SDK.IPC/bin/Debug/net10.0/ConstellaTTS.SDK.IPC.dll"

#nullable enable

using System.Runtime.CompilerServices;
using System.Text.Json;
using ConstellaTTS.SDK.IPC;

// ── Resolve paths relative to this script ─────────────────────────────────────

static string GetScriptPath([CallerFilePath] string path = "") => path;

var scriptPath = GetScriptPath();
var infraDir   = Path.GetDirectoryName(Path.GetFullPath(scriptPath))!;
var root       = Path.GetFullPath(Path.Combine(infraDir, ".."));
var pythonExe  = Path.Combine(infraDir, "python", "python.exe");
var daemonPy   = Path.Combine(root, "src", "ConstellaTTS.Daemon", "main.py");

Console.WriteLine($"[test] python: {pythonExe}");
Console.WriteLine($"[test] daemon: {daemonPy}");
Console.WriteLine();

// ── Helper: pretty-print response data ────────────────────────────────────────

static string Pretty(object? data)
{
    if (data == null) return "null";
    return JsonSerializer.Serialize(data, new JsonSerializerOptions { WriteIndented = false });
}

// ── Run ───────────────────────────────────────────────────────────────────────

async Task RunTestsAsync(string pythonExe, string daemonPy)
{
    var client = new IPCClient(pythonExe, daemonPy);
    try
    {
        Console.WriteLine("[test] starting daemon...");
        await client.StartAsync();
        Console.WriteLine($"[test] connected = {client.IsConnected}");
        Console.WriteLine();

        // Test 1 — echo.ping (no payload)
        {
            var resp = await client.RequestAsync("echo.ping");
            Console.WriteLine($"[test] echo.ping  → ok={resp.Ok}, data={Pretty(resp.Data)}");
        }

        // Test 2 — echo.say (with payload)
        {
            var resp = await client.RequestAsync("echo.say", new Dictionary<string, object>
            {
                ["msg"] = "Merhaba dünya",
                ["foo"] = 42,
            });
            Console.WriteLine($"[test] echo.say   → ok={resp.Ok}, data={Pretty(resp.Data)}");
        }

        // Test 3 — unknown route (expect structured error)
        {
            var resp = await client.RequestAsync("nosuch.route");
            var errType = resp.Error == null ? "(no error)" : resp.Error.Type;
            var errMsg  = resp.Error == null ? ""           : resp.Error.Message;
            Console.WriteLine($"[test] nosuch     → ok={resp.Ok}, error={errType}: {errMsg}");
        }

        // Test 4 — unknown action on known route
        {
            var resp = await client.RequestAsync("echo.doesnotexist");
            var errType = resp.Error == null ? "(no error)" : resp.Error.Type;
            var errMsg  = resp.Error == null ? ""           : resp.Error.Message;
            Console.WriteLine($"[test] bad action → ok={resp.Ok}, error={errType}: {errMsg}");
        }

        // Test 5 — concurrent requests (correlation stress test)
        {
            Console.WriteLine("[test] concurrent: firing 10 pings at once...");
            var tasks = new List<Task<IPCResponse>>();
            for (int i = 0; i < 10; i++)
                tasks.Add(client.RequestAsync("echo.say",
                    new Dictionary<string, object> { ["n"] = i }));

            var results = await Task.WhenAll(tasks);
            var okCount = results.Count(r => r.Ok);
            Console.WriteLine($"[test] concurrent → {okCount}/10 ok");
        }

        // Test 6 — streaming via fake.generate
        {
            Console.WriteLine("[test] streaming: fake.generate 'Hello'...");
            await using var stream = await client.StartStreamAsync(
                "fake.generate",
                new Dictionary<string, object> { ["text"] = "Hello" });

            Console.WriteLine($"[test]   job_id={stream.JobId}");

            var chunks = new List<string>();
            string? terminalType = null;
            await foreach (var evt in stream.ReadEventsAsync())
            {
                if (evt.Type == "chunk" && evt.Data is IDictionary<object, object> d
                    && d.TryGetValue("char", out var c))
                {
                    chunks.Add(c?.ToString() ?? "");
                }
                if (evt.IsTerminal) terminalType = evt.Type;
            }
            var received = string.Concat(chunks);
            Console.WriteLine(
                $"[test] streaming → received='{received}', terminal={terminalType}, " +
                $"count={chunks.Count}");
        }

        // Test 7 — cancel mid-stream
        {
            Console.WriteLine("[test] cancel: fake.generate then abort...");
            await using var stream = await client.StartStreamAsync(
                "fake.generate",
                new Dictionary<string, object>
                {
                    ["text"] = "This is a long string we will cancel early",
                });

            var chunks = 0;
            await foreach (var evt in stream.ReadEventsAsync())
            {
                if (evt.Type == "chunk") chunks++;
                if (chunks >= 3)
                {
                    Console.WriteLine($"[test]   cancelling after {chunks} chunks...");
                    await stream.CancelAsync();
                    break;
                }
            }
            Console.WriteLine($"[test] cancel → received {chunks} chunks before cancel");
        }

        // Test 8 — list_jobs after cancellation (should be empty)
        {
            var resp = await client.RequestAsync("fake.list_jobs");
            Console.WriteLine($"[test] list_jobs  → {Pretty(resp.Data)}");
        }

        Console.WriteLine();
        Console.WriteLine("[test] stopping daemon...");
        await client.StopAsync();
        Console.WriteLine("[test] done.");
    }
    finally
    {
        await client.DisposeAsync();
    }
}

await RunTestsAsync(pythonExe, daemonPy);
