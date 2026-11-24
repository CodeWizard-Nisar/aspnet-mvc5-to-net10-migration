namespace ConfReg.Data.Configuration
{
    /// <summary>
    /// The shape of ConfReg's configuration, as a plain object. Section name "ConfReg".
    ///  - Modern hosts bind it with IOptions:  services.Configure&lt;ConfRegOptions&gt;(config.GetSection("ConfReg"))
    ///  - The legacy host fills it from Web.config / secrets.config / environment (AppConfigLoader).
    /// Environment variables use the same names on both: ConfReg__ConnectionString, ConfReg__HoldMinutes, ...
    /// No secret has a default here: a missing secret must fail loudly at first use, not silently use a placeholder.
    /// </summary>
    public class ConfRegOptions
    {
        public const string SectionName = "ConfReg";

        public string ConnectionString { get; set; }
        public int HoldMinutes { get; set; } = 30;
        public int MaxTalksPerRegistration { get; set; } = 5;
        public string PaymentApiKey { get; set; }
        public string LegacyCryptoKey { get; set; }
        public string LegacyCryptoIv { get; set; }

        public decimal DefaultPriceStandard { get; set; } = 299m;
        public decimal DefaultPriceWorkshop { get; set; } = 499m;
        public decimal DefaultPriceStudent { get; set; } = 99m;
    }
}
