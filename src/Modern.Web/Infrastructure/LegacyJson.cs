using System.Globalization;
using Newtonsoft.Json;
using Newtonsoft.Json.Serialization;

namespace ConfReg.Modern.Infrastructure;

/// <summary>
/// The legacy JSON wire format, reproduced explicitly. Legacy.Web's Json() uses System.Web's JavaScriptSerializer, whose output
/// differs from ASP.NET Core's defaults in ways that silently break any front-end code that parses it:
///
///   property names   PascalCase as declared        (System.Text.Json and Core's Newtonsoft default: camelCase)
///   dates            "\/Date(1805187600000)\/"     (System.Text.Json: ISO 8601; Newtonsoft's default: an offset suffix)
///   enums            numbers, e.g. "Ticket": 3      (System.Text.Json default is also numbers; stated here so it never changes)
///   decimals         299 and 37080.00               (Newtonsoft's default forces "299.0")
///   nulls            written as null, not omitted
///
/// TWO of these were found by the Phase 2 snapshots failing, not by reading documentation, and both are the kind of
/// difference that no compiler and no unit test would ever report (see docs/05-strangler.md):
///   - the offset suffix on dates, fixed by DateTimeZoneHandling.Utc below;
///   - "299.0" where the legacy app writes "299", fixed by LegacyDecimalConverter below.
///
/// Every setting below is explicit rather than defaulted, so a framework upgrade that changes a default cannot change the bytes.
/// The Phase 2 snapshots, unchanged since Phase 2, are what prove this matches.
/// </summary>
public static class LegacyJson
{
    public static readonly JsonSerializerSettings Settings = Create();

    public static JsonSerializerSettings Create() => new()
    {
        // PascalCase, exactly as the C# property is spelled. No camelCasing, no [JsonProperty] renames honoured by accident.
        ContractResolver = new DefaultContractResolver { NamingStrategy = new DefaultNamingStrategy() },

        // "\/Date(ms)\/": Newtonsoft's Microsoft date format is the same family as JavaScriptSerializer's, but NOT the same
        // bytes by default. Dates come out of SQL as Kind=Unspecified, and for Unspecified or Local kinds Newtonsoft appends
        // the server's offset: "\/Date(1805187600000+0000)\/". JavaScriptSerializer never writes an offset. Utc here means
        // "treat Unspecified as UTC and write no offset", which reproduces the legacy bytes exactly.
        // (These columns ARE UTC: the schema stores UTC and the column names say so.)
        DateFormatHandling = DateFormatHandling.MicrosoftDateFormat,
        DateTimeZoneHandling = DateTimeZoneHandling.Utc,

        NullValueHandling = NullValueHandling.Include,
        Formatting = Formatting.None,           // one line, no whitespace, like the original
        StringEscapeHandling = StringEscapeHandling.Default,
        // No cycles can occur: only flat view models are serialised. If one ever did, fail loudly instead of emitting partial JSON.
        ReferenceLoopHandling = ReferenceLoopHandling.Error,
        MissingMemberHandling = MissingMemberHandling.Ignore,
        // No type names in the payload (a security risk and not part of the legacy format).
        TypeNameHandling = TypeNameHandling.None,
        Converters = { new LegacyDecimalConverter() },
    };

    public static string Serialize(object model) => JsonConvert.SerializeObject(model, Settings);
}

/// <summary>
/// Writes a decimal the way JavaScriptSerializer does: the value's own ToString, with no decimal point invented.
/// Newtonsoft writes an integral decimal as "299.0" (so it round-trips back to decimal rather than int); the legacy app
/// writes "299". A price shown as 299.0 in a UI, or compared as a string, is a real regression, and it is invisible until
/// someone looks. Scale that the value genuinely carries is preserved either way: SQL's SUM gives 37080.00, written as such.
/// </summary>
public class LegacyDecimalConverter : JsonConverter
{
    public override bool CanConvert(Type objectType) =>
        objectType == typeof(decimal) || objectType == typeof(decimal?);

    public override bool CanRead => false;

    public override void WriteJson(JsonWriter writer, object value, JsonSerializer serializer)
    {
        if (value is null) writer.WriteNull();
        else writer.WriteRawValue(((decimal)value).ToString(CultureInfo.InvariantCulture));
    }

    public override object ReadJson(JsonReader reader, Type objectType, object existingValue, JsonSerializer serializer) =>
        throw new NotSupportedException();
}
