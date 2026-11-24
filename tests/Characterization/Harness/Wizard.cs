using System.Text.Json;

namespace Characterization.Harness;

public sealed record TalkInfo(int Id, string Title, long StartMs, int Minutes, int Capacity, int Remaining, bool Full)
{
    public long EndMs => StartMs + Minutes * 60_000L;
    public bool Overlaps(TalkInfo o) => StartMs < o.EndMs && o.StartMs < EndMs;
}

public static class Wizard
{
    public static readonly (string, string)[] ValidDetails =
    {
        ("FirstName", "Test"), ("LastName", "Person"), ("Email", "test.person@example.org"),
        ("Company", "Acme"), ("JobTitle", "Engineer"), ("DietaryRequirements", "None")
    };

    /// <summary>Step 1 and 2 done, ready for talks.</summary>
    public static async Task<ConfRegClient> ReachStep3(string ticket = "Standard", string email = "test.person@example.org")
    {
        var c = new ConfRegClient();
        var details = ValidDetails.Select(d => d.Item1 == "Email" ? ("Email", email) : d).ToArray();
        var r1 = await c.Post("/Register/Step1", details);
        if (r1.Code != 302) throw new InvalidOperationException("Step1 did not redirect: " + r1.Body);
        var r2 = await c.Post("/Register/Step2", ("ticketType", ticket));
        if (r2.Code != 302) throw new InvalidOperationException("Step2 did not redirect: " + r2.Body);
        return c;
    }

    public static async Task<List<TalkInfo>> Talks(ConfRegClient c)
    {
        var r = await c.Get("/Register/Step3");
        return ParseTalks(r.Root);
    }

    /// <summary>The talk list as an anonymous visitor sees it. Step 3 needs wizard state, so walk steps 1 and 2 first.</summary>
    public static async Task<List<TalkInfo>> ParseTalksAsync(ConfRegClient c)
    {
        await c.Post("/Register/Step1", ("FirstName", "Observer"), ("LastName", "Only"), ("Email", "observer@example.org"));
        await c.Post("/Register/Step2", ("ticketType", "Standard"));
        return await Talks(c);
    }

    public static List<TalkInfo> ParseTalks(JsonElement step3) =>
        step3.GetProperty("Talks").EnumerateArray().Select(t => new TalkInfo(
            t.GetProperty("Id").GetInt32(),
            t.GetProperty("Title").GetString()!,
            ParseDate(t.GetProperty("StartsUtc").GetString()!),
            t.GetProperty("DurationMinutes").GetInt32(),
            t.GetProperty("Capacity").GetInt32(),
            t.GetProperty("Remaining").GetInt32(),
            t.GetProperty("Full").GetBoolean())).ToList();

    /// <summary>"/Date(1805187600000)/" (as decoded from the wire's "\/Date(ms)\/") to epoch milliseconds.</summary>
    public static long ParseDate(string s) => long.Parse(s.Replace("/Date(", "").Replace(")/", ""));

    public static Task<Reply> PickTalks(ConfRegClient c, params int[] ids) =>
        c.PostForm("/Register/Step3", ids.Select(i => KeyValuePair.Create("talkIds", i.ToString())));

    /// <summary>Greedy pick of n talks that do not overlap and are not full.</summary>
    public static List<TalkInfo> NonOverlapping(IEnumerable<TalkInfo> talks, int n)
    {
        var picked = new List<TalkInfo>();
        foreach (var t in talks.Where(t => !t.Full).OrderBy(t => t.StartMs).ThenBy(t => t.Id))
            if (picked.All(p => !p.Overlaps(t))) { picked.Add(t); if (picked.Count == n) break; }
        return picked;
    }

    public static (TalkInfo, TalkInfo) OverlappingPair(IEnumerable<TalkInfo> talks)
    {
        var l = talks.Where(t => !t.Full).OrderBy(t => t.Id).ToList();
        foreach (var a in l) foreach (var b in l) if (a.Id < b.Id && a.Overlaps(b)) return (a, b);
        throw new InvalidOperationException("seed has no overlapping pair");
    }

    /// <summary>Whole wizard, to a Pending registration at step 4. Returns the client and the reference code.</summary>
    public static async Task<(ConfRegClient Client, string Code)> ReachStep4(string ticket = "Standard", params int[] talkIds)
    {
        var c = await ReachStep3(ticket);
        var r = await PickTalks(c, talkIds);
        if (r.Code != 302) throw new InvalidOperationException("Step3 did not redirect: " + r.Body);
        var s4 = await c.Get("/Register/Step4");
        return (c, s4.Root.GetProperty("ReferenceCode").GetString()!);
    }

    public static async Task<string> Pay(ConfRegClient c, string card = "4242 4242 4242 4242")
    {
        var r = await c.Post("/Register/Step4", ("cardNumber", card));
        if (r.Code != 302) throw new InvalidOperationException("Payment did not redirect: " + r.Body);
        return r.Location!;
    }
}
