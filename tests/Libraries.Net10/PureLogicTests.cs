using ConfReg.Data.Configuration;
using ConfReg.Domain.Abstractions;
using ConfReg.Domain.Entities;
using ConfReg.Domain.Rules;
using ConfReg.Domain.Security;
using ConfReg.Domain.Wizard;
using Xunit;

namespace Libraries.Net10;

/// <summary>The rules and helpers that need no database, running on .NET 10.</summary>
public class PureLogicTests
{
    private static Talk T(int id, int hour, int minute, int length, int cap = 10) => new Talk
    {
        Id = id, Title = "T" + id, StartsUtc = new DateTime(2027, 3, 16, hour, minute, 0, DateTimeKind.Utc), DurationMinutes = length, Capacity = cap
    };

    [Fact]
    public void Talks_that_only_touch_do_not_overlap_but_real_overlaps_do()
    {
        Assert.False(TalkRules.Overlaps(T(1, 9, 0, 60), T(2, 10, 0, 60)));
        Assert.True(TalkRules.Overlaps(T(1, 10, 0, 90), T(2, 10, 30, 60)));
        Assert.True(TalkRules.Overlaps(T(2, 10, 30, 60), T(1, 10, 0, 90))); // symmetric
    }

    [Fact]
    public void Selection_rules_report_too_many_full_and_overlapping()
    {
        var seats = new Dictionary<int, int> { [3] = 10 };
        var errors = TalkRules.Validate(new[] { T(1, 9, 0, 60), T(2, 9, 30, 60), T(3, 12, 0, 60, cap: 10) }, seats, maxTalks: 2);
        Assert.Contains("You can select at most 2 talks.", errors);
        Assert.Contains("\"T3\" is full.", errors);
        Assert.Contains(errors, e => e.Contains("overlap"));
    }

    [Theory]
    [InlineData("a@uni.edu", true)]
    [InlineData("a@UNI.EDU", true)]
    [InlineData("a@ox.ac.uk", false)]
    [InlineData("a@edu.example.com", false)]
    [InlineData("", false)]
    [InlineData("not-an-email", false)]
    public void Student_email_rule(string email, bool expected) => Assert.Equal(expected, TicketRules.IsEduEmail(email));

    [Fact]
    public void Reference_codes_have_the_expected_shape_and_a_seeded_sequence_is_repeatable()
    {
        Assert.Matches("^CR-[A-HJ-NP-Z2-9]{8}$", ReferenceCodes.Next());
        Assert.Equal(ReferenceCodes.Build(new Random(7)), ReferenceCodes.Build(new Random(7)));
    }

    [Fact]
    public void BCrypt_hashes_verify_are_salted_and_recognised_as_current()
    {
        var h = new BCryptPasswordHasher();
        string a = h.Hash("correct horse"), b = h.Hash("correct horse");
        Assert.NotEqual(a, b); // salted: the same password never produces the same value
        Assert.True(h.Verify("correct horse", a));
        Assert.False(h.Verify("wrong", a));
        Assert.True(h.IsCurrentFormat(a));
        Assert.False(h.Verify("x", "not-a-hash")); // garbage is a failure, not an exception
    }

    [Fact]
    public void The_legacy_cipher_round_trips_and_is_not_mistaken_for_a_bcrypt_hash()
    {
        var legacy = new LegacyPasswordCipher(LibFixture.SettingsFrom("x"));
        string stored = legacy.Encrypt("ConfReg-Demo-1!");
        Assert.False(new BCryptPasswordHasher().IsCurrentFormat(stored));
        Assert.True(legacy.Matches(stored, "ConfReg-Demo-1!"));
        Assert.False(legacy.Matches(stored, "nope"));
        Assert.False(legacy.Matches("%%% not base64 %%%", "x")); // never throws on junk
        Assert.False(legacy.Matches("AAAA", "x"));
    }

    [Fact]
    public void Settings_bind_from_configuration_with_defaults_and_fail_helpfully_when_a_secret_is_missing()
    {
        var s = new ConfRegSettings(new ConfRegOptions());
        Assert.Equal(30, s.HoldMinutes);
        Assert.Equal(5, s.MaxTalksPerRegistration);
        Assert.Equal(299m, s.DefaultPrice(TicketType.Standard));
        Assert.Equal(499m, s.DefaultPrice(TicketType.Workshop));
        Assert.Equal(99m, s.DefaultPrice(TicketType.Student));
        var ex = Assert.Throws<InvalidOperationException>(() => s.PaymentApiKey);
        Assert.Contains("ConfReg__PaymentApiKey", ex.Message); // tells you the env var to set

        Assert.Equal(45, LibFixture.SettingsFrom("x", holdMinutes: 45).HoldMinutes);
    }

    [Fact]
    public void The_wizard_session_works_with_no_web_request_at_all()
    {
        var wizard = new WizardSession(new DictionarySessionStore());

        var snapshot = wizard.Load();
        snapshot.TicketType = TicketType.Workshop;
        // Phase 5: writing back is EXPLICIT. With a shared or serialised session the object you read is a COPY, so a
        // mutation that is not saved is lost; code meant to run across processes must not rely on in-process aliasing.
        Assert.Null(wizard.Load().TicketType);
        wizard.Save(snapshot);
        Assert.Equal(TicketType.Workshop, wizard.Load().TicketType);

        wizard.Clear();
        Assert.Null(wizard.Load().TicketType);
    }

    [Fact]
    public void App_paths_resolve_under_the_directory_the_host_chooses()
    {
        var root = Path.Combine(Path.GetTempPath(), "x");
        Assert.Equal(Path.Combine(root, "App_Data", "p.log"), new DirectoryAppPaths(root).Resolve("~/App_Data/p.log"));
    }
}
