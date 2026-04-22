#!/usr/bin/env dotnet-script
// Minimal: just spawn daemon, read PID, try to connect. No SDK involvement.
using System.Diagnostics;
using System.IO.Pipes;
using System.Runtime.CompilerServices;

static string GetScriptPath([CallerFilePath] string path = "") => path;
var infraDir = Path.GetDirectoryName(Path.GetFullPath(GetScriptPath()))!;
var root     = Path.GetFullPath(Path.Combine(infraDir, ".."));
var pythonExe = Path.Combine(infraDir, "python", "python.exe");
var daemonPy  = Path.Combine(root, "src", "ConstellaTTS.Daemon", "main.py");

Console.WriteLine($"Spawning {pythonExe} {daemonPy}");

var psi = new ProcessStartInfo
{
    FileName = pythonExe,
    Arguments = $"\"{daemonPy}\"",
    WorkingDirectory = Path.GetDirectoryName(daemonPy)!,
    RedirectStandardInput = true,
    RedirectStandardOutput = true,
    RedirectStandardError = true,
    UseShellExecute = false,
    CreateNoWindow = true,
};
psi.Environment["PYTHONUNBUFFERED"] = "1";
psi.Environment["PYTHONIOENCODING"] = "utf-8";

var proc = Process.Start(psi)!;
Console.WriteLine($"Daemon pid={proc.Id}");

// Forward stderr
_ = Task.Run(async () =>
{
    string? line;
    while ((line = await proc.StandardError.ReadLineAsync()) is not null)
        Console.Error.WriteLine($"[stderr] {line}");
});

var pipePath = $@"\\.\pipe\constella_{proc.Id}_control";
Console.WriteLine($"Connecting to: {pipePath}");

var pipe = new NamedPipeClientStream(
    ".", $"constella_{proc.Id}_control",
    PipeDirection.InOut, PipeOptions.Asynchronous);

try
{
    await pipe.ConnectAsync(TimeSpan.FromSeconds(10));
    Console.WriteLine("CONNECTED!");
}
catch (Exception ex)
{
    Console.WriteLine($"FAILED: {ex.Message}");
}
finally
{
    try { proc.StandardInput.Close(); } catch { }
    try { proc.Kill(); } catch { }
}
