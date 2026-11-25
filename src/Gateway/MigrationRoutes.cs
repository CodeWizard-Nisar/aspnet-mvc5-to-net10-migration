using Yarp.ReverseProxy.Configuration;

namespace ConfReg.Gateway;

public enum Target { Legacy, Modern }

/// <summary>One migratable unit: a name an operator can use in configuration, and the URL paths it owns.</summary>
public record Page(string Name, string[] Paths, Target Default = Target.Legacy);

/// <summary>
/// THE MIGRATION TABLE. Every URL that has moved, or can move, to Modern.Web is listed here, per page. Anything not listed goes
/// to Legacy.Web through the catch-all route, so a page that nobody has thought about yet keeps working. That default is the
/// whole safety property of a strangler migration: the old app serves everything until a page is explicitly cut over.
/// </summary>
public static class MigrationRoutes
{
    public static readonly Page[] Pages =
    {
        new("Dashboard",     new[] { "/Admin", "/Admin/Index" }),
        new("Settings",      new[] { "/Admin/Settings" }),
        new("Attendees",     new[] { "/Attendees/{**rest}" }),
        new("Talks",         new[] { "/Talks/{**rest}" }),
        new("Registrations", new[] { "/Registrations/{**rest}" }),
        new("Account",       new[] { "/Account/{**rest}" }),
        // Exists only in Modern.Web: nothing to roll back to.
        new("Diagnostics",   new[] { "/Admin/Diagnostics/{**rest}" }, Target.Modern),
    };

    public const string LegacyCluster = "legacy";
    public const string ModernCluster = "modern";

    /// <summary>Resolve every page to a target. Configuration wins; an unknown page name in configuration is an error, not a silent no-op.</summary>
    public static Dictionary<string, Target> Resolve(IConfiguration config)
    {
        var configured = config.GetSection("Migration:Pages").GetChildren().ToDictionary(c => c.Key, c => c.Value, StringComparer.OrdinalIgnoreCase);
        var unknown = configured.Keys.Where(k => Pages.All(p => !p.Name.Equals(k, StringComparison.OrdinalIgnoreCase))).ToList();
        if (unknown.Count > 0)
            throw new InvalidOperationException($"Migration:Pages names unknown page(s): {string.Join(", ", unknown)}. Known: {string.Join(", ", Pages.Select(p => p.Name))}.");

        var targets = Pages.ToDictionary(p => p.Name, p =>
            configured.TryGetValue(p.Name, out var v) && !string.IsNullOrWhiteSpace(v)
                ? Enum.Parse<Target>(v, ignoreCase: true)
                : p.Default);

        Validate(targets);
        return targets;
    }

    /// <summary>
    /// Refuse a route combination that cannot work, at start-up, instead of serving it and letting an admin discover it.
    ///
    /// The one that bites: the two apps PROVE a login differently. Legacy.Web trusts Session["AdminUserId"]; Modern.Web
    /// trusts its own authentication cookie. While the login page is on Legacy, Modern.Web can accept the legacy session
    /// (Auth:AcceptLegacySession), so pages move across freely. But once Account is on Modern, an admin holds a cookie the
    /// legacy app cannot read, so any admin page still on - or rolled BACK to - Legacy answers 401 to a signed-in admin.
    ///
    /// Hence the sequencing rule, which this check enforces: move Account LAST, and roll it back FIRST.
    /// (Found by actually rolling Talks back after Account had moved, and getting a 401 as a logged-in admin.)
    /// </summary>
    private static void Validate(Dictionary<string, Target> targets)
    {
        if (targets["Account"] != Target.Modern) return;

        var strandedOnLegacy = targets
            .Where(kv => kv.Value == Target.Legacy && !kv.Key.Equals("Account", StringComparison.Ordinal))
            .Select(kv => kv.Key)
            .ToList();
        if (strandedOnLegacy.Count == 0) return;

        throw new InvalidOperationException(
            $"Unsafe migration routing: Account is on Modern, but {string.Join(", ", strandedOnLegacy)} " +
            "is still on Legacy. A signed-in admin would hold a Modern authentication cookie that the legacy app cannot " +
            "read, so those pages would answer 401. Either move those pages to Modern as well, or move Account back to " +
            "Legacy (with Auth:AcceptLegacySession=true on Modern.Web) and roll the others back after it.");
    }

    public static (IReadOnlyList<RouteConfig> Routes, IReadOnlyList<ClusterConfig> Clusters) Build(IConfiguration config)
    {
        var targets = Resolve(config);
        var routes = new List<RouteConfig>();
        foreach (var page in Pages)
        {
            string cluster = targets[page.Name] == Target.Modern ? ModernCluster : LegacyCluster;
            for (int i = 0; i < page.Paths.Length; i++)
                routes.Add(new RouteConfig
                {
                    RouteId = $"{page.Name}-{i}",
                    ClusterId = cluster,
                    Match = new RouteMatch { Path = page.Paths[i] }
                });
        }
        // Everything else: the legacy app. Order is high so every specific route above wins first.
        routes.Add(new RouteConfig { RouteId = "legacy-catch-all", ClusterId = LegacyCluster, Order = 1000, Match = new RouteMatch { Path = "/{**catch-all}" } });

        var clusters = new[]
        {
            Cluster(LegacyCluster, config["Migration:Destinations:Legacy"] ?? "http://localhost:5080"),
            Cluster(ModernCluster, config["Migration:Destinations:Modern"] ?? "http://localhost:5100"),
        };
        return (routes, clusters);
    }

    private static ClusterConfig Cluster(string id, string url) => new()
    {
        ClusterId = id,
        Destinations = new Dictionary<string, DestinationConfig> { ["d1"] = new() { Address = url } }
    };
}
