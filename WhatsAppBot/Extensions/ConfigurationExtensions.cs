using System;
using System.IO;

namespace WhatsAppBot.Extensions
{
    public static class ConfigurationExtensions
    {
        public static void LoadDotEnv(string filePath = ".env")
        {
            if (!File.Exists(filePath))
                return;

            foreach (var line in File.ReadAllLines(filePath))
            {
                if (string.IsNullOrWhiteSpace(line) || line.StartsWith("#"))
                    continue;

                var parts = line.Split('=', 2, StringSplitOptions.RemoveEmptyEntries);
                if (parts.Length == 2)
                {
                    var key = parts[0].Trim();
                    var value = parts[1].Trim();

                    // Remove quotes if present
                    if (value.StartsWith("\"") && value.EndsWith("\""))
                        value = value[1..^1];

                    Environment.SetEnvironmentVariable(key, value);
                }
            }
        }

        public static string GetConfigValue(this IConfiguration config, string key, string? envKey = null)
        {
            // Try environment variable first
            var envValue = Environment.GetEnvironmentVariable(envKey ?? key);
            if (!string.IsNullOrEmpty(envValue))
                return envValue;

            // Fallback to appsettings
            return config[key] ?? string.Empty;
        }
    }
}