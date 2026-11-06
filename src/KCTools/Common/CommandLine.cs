using System;
using System.Collections.Generic;
using System.Text;

namespace KCTools.Common
{
    /// <summary>Komut satırlarını tırnak kurallarına uyarak ayırır.</summary>
    public static class CommandLine
    {
        public static List<string> Split(string commandLine)
        {
            var result = new List<string>();

            if (string.IsNullOrWhiteSpace(commandLine))
            {
                return result;
            }

            var current = new StringBuilder();
            var inQuotes = false;

            for (var i = 0; i < commandLine.Length; i++)
            {
                var c = commandLine[i];

                if (c == '"')
                {
                    inQuotes = !inQuotes;
                    continue;
                }

                if (!inQuotes && char.IsWhiteSpace(c))
                {
                    if (current.Length > 0)
                    {
                        result.Add(current.ToString());
                        current.Clear();
                    }

                    continue;
                }

                current.Append(c);
            }

            if (current.Length > 0)
            {
                result.Add(current.ToString());
            }

            return result;
        }

        public static string Join(IEnumerable<string> parts)
        {
            var builder = new StringBuilder();

            foreach (var part in parts)
            {
                if (builder.Length > 0)
                {
                    builder.Append(' ');
                }

                if (part.IndexOf(' ') >= 0)
                {
                    builder.Append('"').Append(part).Append('"');
                }
                else
                {
                    builder.Append(part);
                }
            }

            return builder.ToString();
        }
    }
}
