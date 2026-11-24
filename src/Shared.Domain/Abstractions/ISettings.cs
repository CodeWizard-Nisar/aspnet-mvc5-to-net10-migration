using ConfReg.Domain.Entities;

namespace ConfReg.Domain.Abstractions
{
    /// <summary>
    /// Every deployment-level setting the application reads, in one typed place. Replaces ~10 inline
    /// ConfigurationManager.AppSettings reads (BASELINE-PROBLEM #3). The legacy host fills it from Web.config,
    /// secrets.config and environment variables; modern hosts bind it from IOptions. Same names either way.
    ///
    /// Note what this is NOT: ticket prices and the registration window are edited at runtime from the admin
    /// Settings page and live in the database. ISettings only supplies their defaults.
    /// </summary>
    public interface ISettings
    {
        string ConnectionString { get; }
        int HoldMinutes { get; }
        int MaxTalksPerRegistration { get; }
        string PaymentApiKey { get; }

        /// <summary>Only needed to read admin passwords written before BCrypt. See LegacyPasswordCipher.</summary>
        string LegacyCryptoKey { get; }
        string LegacyCryptoIv { get; }

        decimal DefaultPrice(TicketType type);
    }
}
