using System;
using ConfReg.Domain.Abstractions;
using ConfReg.Domain.Entities;

namespace ConfReg.Data.Configuration
{
    /// <summary>The one ISettings implementation, over a ConfRegOptions. Used by every host; only the loading differs.</summary>
    public class ConfRegSettings : ISettings
    {
        private readonly ConfRegOptions _o;
        public ConfRegSettings(ConfRegOptions options) { _o = options ?? throw new ArgumentNullException("options"); }

        public string ConnectionString { get { return Require(_o.ConnectionString, "ConnectionString"); } }
        public string PaymentApiKey { get { return Require(_o.PaymentApiKey, "PaymentApiKey"); } }
        public string LegacyCryptoKey { get { return Require(_o.LegacyCryptoKey, "LegacyCryptoKey"); } }
        public string LegacyCryptoIv { get { return Require(_o.LegacyCryptoIv, "LegacyCryptoIv"); } }
        public int HoldMinutes { get { return _o.HoldMinutes; } }
        public int MaxTalksPerRegistration { get { return _o.MaxTalksPerRegistration; } }

        public decimal DefaultPrice(TicketType type)
        {
            switch (type)
            {
                case TicketType.Workshop: return _o.DefaultPriceWorkshop;
                case TicketType.Student: return _o.DefaultPriceStudent;
                default: return _o.DefaultPriceStandard;
            }
        }

        private static string Require(string value, string name)
        {
            if (string.IsNullOrWhiteSpace(value))
                throw new InvalidOperationException(
                    "Missing setting 'ConfReg:" + name + "'. Set the environment variable ConfReg__" + name +
                    " or add it to secrets.config (copy config/secrets.config.example).");
            return value;
        }
    }
}
