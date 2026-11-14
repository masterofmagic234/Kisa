using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text.Json;

internal static class Program
{
    private static int Main(string[] args)
    {
        var count = 0; var failures = new List<string>(); var groups = new List<string>();
        Action<bool,string> check = (value,message) => { count++; if (!value) failures.Add(message); };
        foreach (var type in Assembly.GetExecutingAssembly().GetTypes().Where(t => t.Name.EndsWith("Cases",StringComparison.Ordinal)).OrderBy(t=>t.Name))
        {
            var method = type.GetMethod("Run", BindingFlags.Public | BindingFlags.Static);
            if (method == null) continue;
            groups.Add(type.Name);
            try { method.Invoke(null, new object[] { check }); }
            catch (Exception e) { failures.Add(type.Name + ": " + (e.InnerException ?? e)); }
        }
        if (count == 0) failures.Add("No behavior cases ran.");
        var report = new {
            status = failures.Count == 0 ? "PASS" : "FAIL", checks = count, groups,
            failures, compiler_target = "C# 9 / .NET Standard 2.1 core",
            execution_host = ".NET 10", unity_editor_verified = false,
            scope = "Independent models only: no Unity lifecycle, physics, shader, asset import or platform build verification."
        };
        var json = JsonSerializer.Serialize(report, new JsonSerializerOptions { WriteIndented = true });
        Console.WriteLine(json);
        if (args.Length > 0)
        {
            Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(args[0])));
            File.WriteAllText(args[0],json + Environment.NewLine);
        }
        return failures.Count == 0 ? 0 : 1;
    }
}
