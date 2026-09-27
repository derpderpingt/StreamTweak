using System;
using System.Collections.Generic;
using System.IO;
using System.Text;

namespace StreamTweak
{
    public enum PyroWaveState
    {
        /// <summary>No sunshine.conf was found — there is nothing to read or write.</summary>
        NoConfig,
        /// <summary>The <c>pyrowave</c> key is absent or set to anything but "enabled".</summary>
        Disabled,
        Enabled
    }

    /// <summary>
    /// Reads and writes the <c>pyrowave</c> key in the streaming server's sunshine.conf.
    ///
    /// PyroWave (Themaister's GPU wavelet codec, github.com/Themaister/pyrowave) is not part of
    /// Sunshine or any of the forks StreamTweak supports. It exists today in an experimental
    /// Vibepollo build (joemossjr16/pyrowave-streaming, 2026-09-24), opted into with
    /// <c>pyrowave = enabled</c>, and is negotiated between that server and a PyroWave-capable
    /// Moonlight build. StreamTweak only flips the switch: it cannot tell whether the installed
    /// server understands the key, and a server that does not simply logs it as unrecognised.
    ///
    /// Two things follow from the codec itself. Every frame is a keyframe, so it needs hundreds of
    /// Mbps — a wired gigabit link at least (<see cref="MinRecommendedMbps"/>). And the server reads
    /// sunshine.conf at startup, so a change applies after the server restarts.
    /// </summary>
    public static class PyroWaveConfig
    {
        public const string Key = "pyrowave";
        private const string EnabledValue = "enabled";

        /// <summary>Below this the link cannot carry PyroWave's bitrate with headroom.</summary>
        public const long MinRecommendedMbps = 1000;

        public static string? FindConfigPath() => SunshineSync.FindServerFile("sunshine.conf");

        public static PyroWaveState Read(out string? configPath)
        {
            configPath = FindConfigPath();
            if (configPath == null) return PyroWaveState.NoConfig;
            try
            {
                string? value = GetValue(File.ReadAllText(configPath), Key);
                return string.Equals(value, EnabledValue, StringComparison.OrdinalIgnoreCase)
                    ? PyroWaveState.Enabled
                    : PyroWaveState.Disabled;
            }
            catch { return PyroWaveState.Disabled; }
        }

        /// <summary>
        /// Turns PyroWave on or off. Returns null on success, an error message otherwise.
        /// Off removes the key rather than writing a value, because the codec is opt-in and the
        /// only value its build documents is "enabled".
        /// </summary>
        public static string? Write(bool enabled)
        {
            string? path = FindConfigPath();
            if (path == null) return "No sunshine.conf found for Sunshine, Apollo, Vibeshine or Vibepollo.";

            string original;
            try { original = File.ReadAllText(path); }
            catch (Exception ex) { return ex.Message; }

            string updated = SetValue(original, Key, enabled ? EnabledValue : null);
            if (updated == original) return null;

            // Same route as apps.json: the service (LocalSystem) for Program Files, a direct
            // write for a config the user owns. No BOM — the server's parser reads the first key
            // byte for byte, and "﻿sunshine_name" is not a key it knows.
            if (SpeedChanger.WriteServerFile(path, updated)) return null;
            try
            {
                File.WriteAllText(path, updated, new UTF8Encoding(false));
                return null;
            }
            catch (Exception ex) { return ex.Message; }
        }

        // ── key = value parsing ───────────────────────────────────────────────

        /// <summary>The value of <paramref name="key"/>, or null. Last occurrence wins, as it
        /// does in the server's own parser.</summary>
        internal static string? GetValue(string content, string key)
        {
            string? value = null;
            foreach (string raw in content.Split('\n'))
                if (TryParseLine(raw, out string k, out string v) && k.Equals(key, StringComparison.OrdinalIgnoreCase))
                    value = v;
            return value;
        }

        /// <summary>
        /// Sets <paramref name="key"/> to <paramref name="value"/>, or removes it when value is
        /// null. Every other line — comments, blank lines, ordering, line endings — is kept.
        /// </summary>
        internal static string SetValue(string content, string key, string? value)
        {
            string newline = content.Contains("\r\n") ? "\r\n" : "\n";
            var lines = new List<string>(content.Split('\n'));
            for (int i = 0; i < lines.Count; i++) lines[i] = lines[i].TrimEnd('\r');

            // A trailing newline leaves an empty last element; keep it aside so an appended key
            // goes before it and the file still ends with a newline.
            bool trailingNewline = lines.Count > 0 && lines[^1].Length == 0;
            if (trailingNewline) lines.RemoveAt(lines.Count - 1);

            bool written = false;
            for (int i = lines.Count - 1; i >= 0; i--)
            {
                if (!TryParseLine(lines[i], out string k, out _) || !k.Equals(key, StringComparison.OrdinalIgnoreCase))
                    continue;
                if (value != null && !written)
                {
                    lines[i] = $"{key} = {value}";
                    written = true;
                }
                else
                {
                    lines.RemoveAt(i);
                }
            }

            if (value != null && !written) lines.Add($"{key} = {value}");

            string result = string.Join(newline, lines);
            if (trailingNewline) result += newline;
            return result;
        }

        private static bool TryParseLine(string line, out string key, out string value)
        {
            key = value = "";
            string s = line.Trim();
            if (s.Length == 0 || s[0] == '#') return false;
            int eq = s.IndexOf('=');
            if (eq <= 0) return false;
            key = s.Substring(0, eq).Trim();
            value = s.Substring(eq + 1).Trim();
            return key.Length > 0;
        }
    }
}
