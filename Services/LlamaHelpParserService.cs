using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Text.RegularExpressions;
using System.Threading.Tasks;

namespace LlamaServerLauncher.Services;

public static class LlamaHelpParserService
{
    private static readonly Regex FlagPattern = new(
        @"(?:^|\s|,)(-[a-zA-Z](?:\s*,\s*|-+))(?=\s|,|$)|(--[\w][\w-]*)",
        RegexOptions.Compiled);

    private static readonly Regex AllFlagsPattern = new(
        @"(-[a-zA-Z]\b)|(--[\w][\w-]*)",
        RegexOptions.Compiled);

    public static async Task<HashSet<string>?> GetSupportedFlagsAsync(string executablePath)
    {
        if (string.IsNullOrWhiteSpace(executablePath))
            return null;

        if (!System.IO.File.Exists(executablePath))
            return null;

        try
        {
            var psi = new ProcessStartInfo
            {
                FileName = executablePath,
                Arguments = "--help",
                UseShellExecute = false,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                CreateNoWindow = true,
                StandardOutputEncoding = System.Text.Encoding.UTF8,
                StandardErrorEncoding = System.Text.Encoding.UTF8
            };

            using var process = new Process { StartInfo = psi };
            process.Start();

            var output = await process.StandardOutput.ReadToEndAsync();
            var error = await process.StandardError.ReadToEndAsync();

            var fullOutput = output + "\n" + error;

            await process.WaitForExitAsync();

            if (process.ExitCode != 0 && string.IsNullOrWhiteSpace(fullOutput))
                return null;

            return ParseFlagsFromHelp(fullOutput);
        }
        catch
        {
            return null;
        }
    }

    public static HashSet<string> ParseFlagsFromHelp(string helpText)
    {
        var flags = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        if (string.IsNullOrWhiteSpace(helpText))
            return flags;

        foreach (var line in helpText.Split('\n'))
        {
            var trimmed = line.TrimStart();
            if (string.IsNullOrWhiteSpace(trimmed))
                continue;

            if (!trimmed.StartsWith("-") && !trimmed.StartsWith("  -"))
                continue;

            var matches = AllFlagsPattern.Matches(trimmed);
            foreach (Match match in matches)
            {
                if (match.Success)
                {
                    var flag = match.Value;
                    if (flag.StartsWith("--") || (flag.Length == 2 && flag[0] == '-'))
                    {
                        if (!IsExcludedFlag(flag))
                        {
                            flags.Add(flag);
                        }
                    }
                }
            }
        }

        return flags;
    }

    private static bool IsExcludedFlag(string flag)
    {
        switch (flag.ToLowerInvariant())
        {
            case "-h":
            case "--help":
            case "--usage":
            case "--version":
            case "--license":
                return true;
            default:
                return false;
        }
    }
}
