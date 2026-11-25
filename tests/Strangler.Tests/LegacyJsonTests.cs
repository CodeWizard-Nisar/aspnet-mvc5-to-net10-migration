using ConfReg.Modern.Infrastructure;
using ConfReg.ViewModels;
using Xunit;

namespace Strangler.Tests;

/// <summary>
/// The legacy wire format, rule by rule. Every assertion here is a byte-level difference that the ASP.NET Core defaults get
/// WRONG, and that no compiler or ordinary unit test would report: the symptom is a front end quietly showing "299.0", or a
/// date parser failing on an offset it did not expect. Two of these were found only because the Phase 2 snapshots failed.
/// </summary>
public class LegacyJsonTests
{
    private class Sample
    {
        public int Id { get; set; }
        public string Title { get; set; }
        public decimal Price { get; set; }
        public DateTime StartsUtc { get; set; }
        public TicketKind Kind { get; set; }
    }

    public enum TicketKind { Standard = 1, Workshop = 2, Student = 3 }

    [Fact]
    public void Property_names_stay_PascalCase_as_declared()
    {
        string json = LegacyJson.Serialize(new Sample { Id = 7, Title = "x" });
        Assert.Contains("\"Id\":7", json);
        Assert.Contains("\"Title\":\"x\"", json);
        Assert.DoesNotContain("\"id\"", json);     // camelCase is ASP.NET Core's default and would break every caller
        Assert.DoesNotContain("\"title\"", json);
    }

    [Theory]
    // An integral decimal must NOT gain a decimal point: Newtonsoft's own default writes "299.0".
    [InlineData(299, "\"Price\":299")]
    [InlineData(-1, "\"Price\":-1")]
    [InlineData(0, "\"Price\":0")]
    public void Integral_decimals_are_written_without_a_decimal_point(int value, string expected) =>
        Assert.Contains(expected, LegacyJson.Serialize(new Sample { Price = value }));

    [Fact]
    public void Scale_the_value_really_carries_is_preserved()
    {
        // SQL's SUM(money-ish decimal) comes back with scale 2, and the legacy app wrote it out as-is.
        Assert.Contains("\"Price\":37080.00", LegacyJson.Serialize(new Sample { Price = 37080.00m }));
        Assert.Contains("\"Price\":99.50", LegacyJson.Serialize(new Sample { Price = 99.50m }));
    }

    [Theory]
    [InlineData(DateTimeKind.Utc)]
    [InlineData(DateTimeKind.Unspecified)] // what EF6 hands back for a datetime column: the risky one
    public void Dates_use_the_microsoft_format_with_no_timezone_offset(DateTimeKind kind)
    {
        var when = new DateTime(2027, 3, 16, 9, 0, 0, kind);
        string json = LegacyJson.Serialize(new Sample { StartsUtc = when });

        Assert.Contains("\"StartsUtc\":\"\\/Date(1805187600000)\\/\"", json);
        Assert.DoesNotContain("+0000", json);                 // Newtonsoft adds an offset for Unspecified/Local by default
        Assert.DoesNotContain("2027-03-16T09:00:00", json);   // and System.Text.Json would write ISO 8601
    }

    [Fact]
    public void Enums_are_numbers_not_names()
    {
        Assert.Contains("\"Kind\":2", LegacyJson.Serialize(new Sample { Kind = TicketKind.Workshop }));
        Assert.DoesNotContain("Workshop", LegacyJson.Serialize(new Sample { Kind = TicketKind.Workshop }));
    }

    [Fact]
    public void Nulls_are_written_rather_than_omitted()
    {
        string json = LegacyJson.Serialize(new Sample { Title = null });
        Assert.Contains("\"Title\":null", json);
    }

    [Fact]
    public void Output_is_compact_single_line()
    {
        string json = LegacyJson.Serialize(new Sample { Id = 1 });
        Assert.DoesNotContain("\n", json);
        Assert.DoesNotContain(": ", json);
    }

    [Fact]
    public void A_real_view_model_round_trips_in_the_legacy_shape()
    {
        var model = new SettingsForm { PriceStandard = 299m, PriceWorkshop = 499m, PriceStudent = 99m,
            OpensUtc = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc), ClosesUtc = new DateTime(2027, 1, 1, 0, 0, 0, DateTimeKind.Utc) };
        string json = LegacyJson.Serialize(model);
        Assert.Contains("\"PriceStandard\":299", json);
        Assert.DoesNotContain("299.0", json);
        Assert.Contains("\"Errors\":[]", json);
    }
}
