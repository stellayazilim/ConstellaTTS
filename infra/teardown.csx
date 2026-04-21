#!/usr/bin/env dotnet-script
// infra/teardown.csx — ConstellaTTS Python portable cleanup
// Usage: dotnet script infra/teardown.csx
//
// Removes the infra/python/ directory. Daemon scripts are preserved.

var pythonDir = Path.Combine(AppContext.BaseDirectory, "python");

if (!Directory.Exists(pythonDir))
{
    Console.WriteLine("[teardown] infra/python/ not found — nothing to do.");
}
else
{
    Console.WriteLine($"[teardown] Removing {pythonDir}...");
    Directory.Delete(pythonDir, recursive: true);
    Console.WriteLine("[teardown] Done.");
}
