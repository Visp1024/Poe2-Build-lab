using PBLEngine;
using PBLHost;
using System;
using System.IO;

if (args.Length > 0 && args[0] == "driver")
    return Driver.Run(args[1..]);

var repoRoot = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "../../../.."));
Console.WriteLine($"Repo root : {repoRoot}");
Console.WriteLine($"src/      : {Path.Combine(repoRoot, "src")}");

// ── Initialize Lua host ───────────────────────────────────────────────────
Console.WriteLine("\nLoading PoB Lua environment via HeadlessWrapper.lua …");
using var host = new LuaHost();
try
{
    host.Initialize(repoRoot);
}
catch (Exception ex)
{
    Console.Error.WriteLine($"\nERROR initializing LuaHost:\n{ex.Message}");
    return 1;
}
Console.WriteLine("Environment loaded.");

// ── Create an empty build ─────────────────────────────────────────────────
Console.WriteLine("Creating empty build …");
var model = new BuildModel(host);
try
{
    model.NewBuild();
}
catch (Exception ex)
{
    Console.Error.WriteLine($"\nERROR creating build:\n{ex.Message}");
    return 1;
}

// ── Print stats from BuildModel properties ────────────────────────────────
Console.WriteLine("\n=== BuildModel stats (typed properties) ===");
Console.WriteLine($"  {"TotalDPS",-24} = {model.TotalDps}");
Console.WriteLine($"  {"AverageDamage",-24} = {model.AverageDamage}");
Console.WriteLine($"  {"Speed",-24} = {model.Speed}");
Console.WriteLine($"  {"CritChance",-24} = {model.CritChance}");
Console.WriteLine($"  {"CritMultiplier",-24} = {model.CritMultiplier}");
Console.WriteLine($"  {"HitChance",-24} = {model.HitChance}");
Console.WriteLine($"  {"Life",-24} = {model.Life}");
Console.WriteLine($"  {"Mana",-24} = {model.Mana}");
Console.WriteLine($"  {"EnergyShield",-24} = {model.EnergyShield}");
Console.WriteLine($"  {"Armour",-24} = {model.Armour}");
Console.WriteLine($"  {"Evasion",-24} = {model.Evasion}");
Console.WriteLine($"  {"PhysicalReduction",-24} = {model.PhysicalReduction}");

Console.WriteLine($"\n  AllStats entries: {model.AllStats.Count}");

// ── INotifyPropertyChanged demo ───────────────────────────────────────────
Console.WriteLine("\n=== INotifyPropertyChanged wiring test ===");
var changes = new System.Collections.Generic.List<string>();
model.PropertyChanged += (_, e) => changes.Add(e.PropertyName ?? "?");

model.NewBuild();   // triggers Refresh() internally → fires events
Console.WriteLine($"  Properties changed after NewBuild(): {string.Join(", ", changes)}");

// ── Run verification tests ────────────────────────────────────────────────
var verifyResult = Verification.Run(host);

Console.WriteLine(verifyResult == 0 ? "✓ All verification tests passed." : "✗ Some verification tests FAILED.");
return verifyResult;
