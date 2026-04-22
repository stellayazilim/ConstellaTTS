#!/usr/bin/env dotnet-script
// infra/teardown.csx — ConstellaTTS Python portable cleanup
// Usage: dotnet script infra/teardown.csx
//
// Removes the infra/python/ directory. Daemon scripts are preserved.

using System.Runtime.CompilerServices;

static string GetScriptPath([CallerFilePath] string path = "") => path;

var scriptPath = GetScriptPath();
var infraDir   = Path.GetDirectoryName(Path.GetFullPath(scriptPath))!;
var pythonDir  = Path.Combine(infraDir, "python");

if (!Directory.Exists(pythonDir))
{
    Console.WriteLine($"[teardown] {pythonDir} not found — nothing to do.");
}
else
{
    Console.WriteLine($"[teardown] Removing {pythonDir}...");
    Directory.Delete(pythonDir, recursive: true);
    Console.WriteLine("[teardown] Done.");
}
