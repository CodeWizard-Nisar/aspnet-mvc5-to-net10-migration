using ConfReg.Gateway;
using Microsoft.Extensions.Configuration;
using Xunit;

namespace Strangler.Tests;

/// <summary>The migration table: what a page defaults to, what configuration may change, and which combinations are refused.</summary>
public class MigrationRoutesTests
{
    private static IConfiguration Config(params (string Page, string Target)[] pages) =>
        new ConfigurationBuilder().AddInMemoryCollection(
            pages.ToDictionary(p => $"Migration:Pages:{p.Page}", p => p.Target)).Build();

    [Fact]
    public void Every_page_starts_on_legacy_so_an_unconfigured_gateway_changes_nothing()
    {
        var targets = MigrationRoutes.Resolve(Config());
        Assert.All(MigrationRoutes.Pages.Where(p => p.Default == Target.Legacy),
            page => Assert.Equal(Target.Legacy, targets[page.Name]));
    }

    [Fact]
    public void Anything_not_in_the_table_falls_through_to_legacy_last()
    {
        var (routes, _) = MigrationRoutes.Build(Config());
        var catchAll = routes.Single(r => r.RouteId == "legacy-catch-all");
        Assert.Equal(MigrationRoutes.LegacyCluster, catchAll.ClusterId);
        Assert.Equal("/{**catch-all}", catchAll.Match.Path);
        // Highest order = matched last, so every explicitly migrated page wins first.
        Assert.All(routes.Where(r => r.RouteId != "legacy-catch-all"),
            r => Assert.True((r.Order ?? 0) < catchAll.Order));
    }

    [Theory]
    [InlineData("Modern")]
    [InlineData("modern")] // configuration is written by humans
    public void A_page_can_be_cut_over_by_configuration_alone(string value)
    {
        var (routes, _) = MigrationRoutes.Build(Config(("Talks", value)));
        Assert.All(routes.Where(r => r.RouteId.StartsWith("Talks-")),
            r => Assert.Equal(MigrationRoutes.ModernCluster, r.ClusterId));
    }

    [Fact]
    public void A_page_can_be_rolled_back_to_legacy_the_same_way()
    {
        var (routes, _) = MigrationRoutes.Build(Config(("Talks", "Legacy"), ("Attendees", "Modern")));
        Assert.All(routes.Where(r => r.RouteId.StartsWith("Talks-")), r => Assert.Equal(MigrationRoutes.LegacyCluster, r.ClusterId));
        Assert.All(routes.Where(r => r.RouteId.StartsWith("Attendees-")), r => Assert.Equal(MigrationRoutes.ModernCluster, r.ClusterId));
    }

    [Fact]
    public void A_misspelled_page_name_is_an_error_not_a_silent_no_op()
    {
        var ex = Assert.Throws<InvalidOperationException>(() => MigrationRoutes.Resolve(Config(("Taks", "Modern"))));
        Assert.Contains("Taks", ex.Message);
        Assert.Contains("Talks", ex.Message); // tells the operator the valid names
    }

    [Fact]
    public void Moving_account_to_modern_while_an_admin_page_stays_on_legacy_is_refused()
    {
        var ex = Assert.Throws<InvalidOperationException>(() =>
            MigrationRoutes.Resolve(Config(("Account", "Modern"), ("Talks", "Legacy"), ("Dashboard", "Modern"),
                ("Settings", "Modern"), ("Attendees", "Modern"), ("Registrations", "Modern"))));
        Assert.Contains("Talks", ex.Message);
        Assert.Contains("401", ex.Message);
    }

    [Fact]
    public void Account_on_legacy_with_pages_split_across_both_apps_is_allowed()
    {
        // The normal mid-migration state: log in on legacy, some pages already modern.
        var targets = MigrationRoutes.Resolve(Config(("Account", "Legacy"), ("Talks", "Modern"), ("Dashboard", "Legacy")));
        Assert.Equal(Target.Legacy, targets["Account"]);
        Assert.Equal(Target.Modern, targets["Talks"]);
    }

    [Fact]
    public void Everything_on_modern_is_allowed_the_end_state()
    {
        var targets = MigrationRoutes.Resolve(Config(MigrationRoutes.Pages.Select(p => (p.Name, "Modern")).ToArray()));
        Assert.All(targets.Values, t => Assert.Equal(Target.Modern, t));
    }
}
