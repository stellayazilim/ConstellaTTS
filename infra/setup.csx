#!/usr/bin/env dotnet-script
// infra/setup.csx — ConstellaTTS dev environment setup
// Usage: dotnet script infra/setup.csx
//
// Downloads embeddable Python 3.11, installs pip, installs daemon requirements.
// Python is installed to infra/python/ — kept out of project root.
// Re-running is safe (idempotent).

using System.IO.Compression;
using System.Net.Http;
using System.Runtime.CompilerServices;

// ── Resolve real script location ──────────────────────────────────────────────
// AppContext.BaseDirectory under `dotnet script` points to the dotnet-script
// tool directory (e.g. C:\Users\<user>\.nuget\packages\dotnet-script\...), NOT
// this script's directory. Use [CallerFilePath] to get where *this file* is.

static string GetScriptPath([CallerFilePath] string path = "") => path;

var scriptPath = GetScriptPath();
var infraDir   = Path.GetDirectoryName(Path.GetFullPath(scriptPath))!;
var root       = Path.GetFullPath(Path.Combine(infraDir, ".."));
var pythonDir  = Path.Combine(infraDir, "python");
var daemonDir  = Path.Combine(root, "src", "ConstellaTTS.Daemon");
var pythonExe  = Path.Combine(pythonDir, "python.exe");
var pipScript  = Path.Combine(pythonDir, "get-pip.py");
var pipExe     = Path.Combine(pythonDir, "Scripts", "pip.exe");
var reqFile    = Path.Combine(daemonDir, "requirements.txt");

Console.WriteLine($"[setup] Script:  {scriptPath}");
Console.WriteLine($"[setup] Root:    {root}");
Console.WriteLine($"[setup] Python:  {pythonDir}");
Console.WriteLine($"[setup] Daemon:  {daemonDir}");

const string PythonVersion = "3.11.9";
const string PythonZipUrl  =
    $"https://www.python.org/ftp/python/{PythonVersion}/python-{PythonVersion}-embed-amd64.zip";

// ── 1. Download + extract Python ─────────────────────────────────────────────

if (!File.Exists(pythonExe))
{
    Console.WriteLine($"[setup] Downloading Python {PythonVersion} embeddable...");
    Directory.CreateDirectory(pythonDir);

    var zipPath = Path.Combine(pythonDir, "python-embed.zip");
    using var http = new HttpClient();
    var bytes = await http.GetByteArrayAsync(PythonZipUrl);
    await File.WriteAllBytesAsync(zipPath, bytes);

    Console.WriteLine("[setup] Extracting...");
    ZipFile.ExtractToDirectory(zipPath, pythonDir, overwriteFiles: true);
    File.Delete(zipPath);
    Console.WriteLine("[setup] Python ready.");
}
else
{
    Console.WriteLine("[setup] Python already installed, skipping.");
}

// ── 2. Enable site-packages ───────────────────────────────────────────────────

var pthFile = Directory.GetFiles(pythonDir, "python*._pth").FirstOrDefault();
if (pthFile is not null)
{
    var content = await File.ReadAllTextAsync(pthFile);
    if (content.Contains("#import site"))
    {
        content = content.Replace("#import site", "import site");
        await File.WriteAllTextAsync(pthFile, content);
        Console.WriteLine("[setup] Enabled site-packages in .pth file.");
    }
}

// ── 3. Install pip ────────────────────────────────────────────────────────────

if (!File.Exists(pipExe))
{
    Console.WriteLine("[setup] Installing pip...");
    using var http = new HttpClient();
    var getPip = await http.GetStringAsync("https://bootstrap.pypa.io/get-pip.py");
    await File.WriteAllTextAsync(pipScript, getPip);

    if (await RunAsync(pythonExe, pipScript) != 0)
    {
        Console.Error.WriteLine("[setup] pip installation failed.");
        return;
    }

    File.Delete(pipScript);
    Console.WriteLine("[setup] pip ready.");
}
else
{
    Console.WriteLine("[setup] pip already installed, skipping.");
}

// ── 4. Install daemon requirements ────────────────────────────────────────────

if (File.Exists(reqFile))
{
    Console.WriteLine("[setup] Installing daemon requirements...");
    if (await RunAsync(pipExe, $"install -r \"{reqFile}\"") != 0)
        Console.Error.WriteLine("[setup] requirements install failed.");
    else
        Console.WriteLine("[setup] Requirements ready.");
}
else
{
    Console.Error.WriteLine($"[setup] Missing requirements file: {reqFile}");
}

Console.WriteLine("[setup] Done. Python lives in " + pythonDir);

// ── Helpers ───────────────────────────────────────────────────────────────────

async Task<int> RunAsync(string exe, string args)
{
    var psi = new System.Diagnostics.ProcessStartInfo(exe, args)
    {
        UseShellExecute = false,
    };
    var p = System.Diagnostics.Process.Start(psi)!;
    await p.WaitForExitAsync();
    return p.ExitCode;
}
