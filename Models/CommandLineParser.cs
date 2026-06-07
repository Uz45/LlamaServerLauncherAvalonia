using System;
using System.Collections.Generic;
using System.Text;

namespace LlamaServerLauncher.Models;

public static class CommandLineParser
{
    /// <summary>
    /// Determines whether a token is a command-line flag (e.g. -v, --threads)
    /// rather than a negative number (e.g. -1, -0.5, -.3).
    /// A token is a flag if it starts with '-' and the next character is a letter or another '-'.
    /// </summary>
    public static bool IsFlag(string token)
    {
        if (string.IsNullOrEmpty(token) || token[0] != '-')
            return false;

        if (token.Length < 2)
            return true; // bare "-" is treated as a flag

        char next = token[1];
        // "-" followed by a digit or '.' is a negative number, not a flag
        if (char.IsDigit(next) || next == '.')
            return false;

        return true;
    }

    public static string NormalizeSpecialCharacters(string input)
    {
        if (string.IsNullOrEmpty(input)) return input;
        
        // First normalize whitespace but preserve backslash sequences that might be JSON escaping
        var result = input
            .Replace("\\t", "\t")
            .Replace("\\n", "\n")
            .Replace("\\r", "\r");
        
        // Then decode JSON-style double escaping (\\ becomes \) that may come from UI JSON input
        // This handles cases where user enters JSON with proper escaping in the UI
        result = result.Replace("\\\\", "\\");
        
        // Also normalize actual tab/newline chars if they somehow got in
        result = result.Replace("\t", " ").Replace("\n", " ").Replace("\r", " ");
        
        return result;
    }

    public static List<string> ParseArguments(string args)
    {
        var result = new List<string>();
        if (string.IsNullOrWhiteSpace(args))
            return result;

        var sb = new StringBuilder();
        bool inQuotes = false;
        char? quoteChar = null;

        for (int i = 0; i < args.Length; i++)
        {
            char c = args[i];
            
            if (inQuotes && c == '\\' && i + 1 < args.Length)
            {
                char nextC = args[i + 1];
                if (nextC == '"' || nextC == '\\')
                {
                    sb.Append(c);
                    sb.Append(nextC);
                    i++;
                    continue;
                }
            }

            if (!inQuotes && (c == '"' || c == '\''))
            {
                inQuotes = true;
                quoteChar = c;
            }
            else if (inQuotes && c == quoteChar)
            {
                inQuotes = false;
                quoteChar = null;
            }
            else if (!inQuotes && (c == ' ' || c == '\t'))
            {
                if (sb.Length > 0)
                {
                    result.Add(sb.ToString());
                    sb.Clear();
                }
            }
            else
            {
                sb.Append(c);
            }
        }

        if (sb.Length > 0)
            result.Add(sb.ToString());

        return result;
    }

    public static Dictionary<string, string?> GetArgumentValues(List<string> args)
    {
        var result = new Dictionary<string, string?>(StringComparer.OrdinalIgnoreCase);

        for (int i = 0; i < args.Count; i++)
        {
            string arg = args[i];

            if (IsFlag(arg))
            {
                if (i + 1 < args.Count && !IsFlag(args[i + 1]))
                {
                    result[arg] = args[i + 1];
                    i++;
                }
                else
                {
                    result[arg] = null;
                }
            }
        }

        return result;
    }

    public static HashSet<string> GetArgumentFlags(List<string> args)
    {
        var flags = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        
        foreach (var arg in args)
        {
            if (IsFlag(arg))
            {
                flags.Add(arg);
            }
        }
        
        return flags;
    }
}