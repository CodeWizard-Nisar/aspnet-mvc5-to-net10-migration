using System.Text;
using System.Text.RegularExpressions;

namespace Characterization.Harness;

[Flags]
public enum Scrub
{
    None = 0,
    /// <summary>Reference codes minted during the test are random: CR-XXXXXXXX becomes &lt;REF&gt;.</summary>
    References = 1,
    /// <summary>Timestamps taken from the real clock (within a day of now) become &lt;NOW&gt;.</summary>
    NowDates = 2,
    /// <summary>The hold countdown on step 4 depends on how long the test took.</summary>
    Countdown = 4,
    All = References | NowDates | Countdown
}

/// <summary>
/// Minimal snapshot testing. The body is kept AS THE SERVER SENT IT (only re-indented), because the wire
/// format is the thing under test: PascalCase names, "\/Date(ms)\/" dates, decimals like 37080.00.
/// A snapshot file is compared byte for byte. Missing or different means a failing test, unless
/// CONFREG_UPDATE_SNAPSHOTS=1, which (re)writes the file. Review every changed snapshot in the diff.
/// </summary>
public static class Snapshot
{
    public static void VerifyJson(string name, string body, Scrub scrub = Scrub.None) =>
        Verify(name + ".json", Indent(Apply(body, scrub)));

    public static void VerifyText(string name, string extension, string body, Scrub scrub = Scrub.None) =>
        Verify(name + "." + extension, Apply(body, scrub).Replace("\r\n", "\n"));

    private static void Verify(string file, string actual)
    {
        var path = Path.Combine(TestEnvironment.SnapshotDir, file);
        if (TestEnvironment.UpdateSnapshots)
        {
            Directory.CreateDirectory(TestEnvironment.SnapshotDir);
            File.WriteAllText(path, actual, new UTF8Encoding(false));
            return;
        }
        if (!File.Exists(path))
            throw new Xunit.Sdk.XunitException($"No snapshot '{file}'. Generate it with CONFREG_UPDATE_SNAPSHOTS=1 and review it.");
        var expected = File.ReadAllText(path).Replace("\r\n", "\n");
        Xunit.Assert.Equal(expected, actual);
    }

    private static string Apply(string body, Scrub scrub)
    {
        if (scrub.HasFlag(Scrub.References)) body = Regex.Replace(body, "CR-[A-HJ-NP-Z2-9]{8}", "<REF>");
        if (scrub.HasFlag(Scrub.Countdown)) body = Regex.Replace(body, "\"SecondsRemaining\":\\d+", "\"SecondsRemaining\":\"<SECS>\"");
        if (scrub.HasFlag(Scrub.NowDates))
        {
            var now = DateTimeOffset.UtcNow;
            body = Regex.Replace(body, @"\\/Date\((-?\d+)\)\\/", m =>
                Math.Abs((DateTimeOffset.FromUnixTimeMilliseconds(long.Parse(m.Groups[1].Value)) - now).TotalDays) < 1
                    ? "<NOW>" : m.Value);
        }
        return body;
    }

    /// <summary>String-aware re-indenter that never rewrites escapes, so "\/Date(..)\/" survives untouched.</summary>
    public static string Indent(string json)
    {
        var sb = new StringBuilder();
        int depth = 0;
        bool inString = false, escaped = false;
        void Line() => sb.Append('\n').Append(' ', depth * 2);

        foreach (var c in json.Trim())
        {
            if (inString)
            {
                sb.Append(c);
                if (escaped) escaped = false;
                else if (c == '\\') escaped = true;
                else if (c == '"') inString = false;
                continue;
            }
            switch (c)
            {
                case '"': inString = true; sb.Append(c); break;
                case '{': case '[': sb.Append(c); depth++; Line(); break;
                case '}': case ']':
                    depth--;
                    Line(); sb.Append(c); break;
                case ',': sb.Append(c); Line(); break;
                case ':': sb.Append(": "); break;
                default: if (!char.IsWhiteSpace(c)) sb.Append(c); break;
            }
        }
        // Empty containers come out as "[\n]" or "{\n}"; tidy them to [] and {}.
        var s = Regex.Replace(sb.ToString(), @"([\[{])\s*\n\s*([\]}])", "$1$2");
        return s + "\n";
    }
}
