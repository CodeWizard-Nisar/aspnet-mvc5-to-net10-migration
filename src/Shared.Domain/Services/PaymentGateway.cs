using System;
using System.IO;
using ConfReg.Domain.Abstractions;

namespace ConfReg.Domain.Services
{
    public class PaymentResult
    {
        public bool Success { get; set; }
        public string ProviderRef { get; set; }
        public string Message { get; set; }
    }

    /// <summary>
    /// Stubbed payment gateway. No real provider is called anywhere in this repo.
    /// A card number ending in 0002 is declined, anything else succeeds.
    ///
    /// Phase 3: the key comes from ISettings (was ConfigurationManager, #3/#4), and the audit line's client IP
    /// and file location come from IClientInfo and IAppPaths (was HttpContext.Current, #1). Nothing here knows
    /// whether it is running inside a web request.
    /// </summary>
    public class PaymentGateway
    {
        private readonly ISettings _settings;
        private readonly IClientInfo _client;
        private readonly IAppPaths _paths;

        public PaymentGateway(ISettings settings, IClientInfo client, IAppPaths paths)
        {
            _settings = settings;
            _client = client;
            _paths = paths;
        }

        public PaymentResult Charge(decimal amount, string cardNumber, string referenceCode)
        {
            if (string.IsNullOrEmpty(_settings.PaymentApiKey)) throw new InvalidOperationException("ConfReg:PaymentApiKey is not configured.");

            string digits = (cardNumber ?? "").Replace(" ", "");
            bool declined = digits.EndsWith("0002");

            Audit(string.Format("CHARGE ref={0} amount={1:0.00} result={2}", referenceCode, amount, declined ? "DECLINED" : "OK"));

            return new PaymentResult
            {
                Success = !declined,
                ProviderRef = declined ? null : "STUB-" + Guid.NewGuid().ToString("N").Substring(0, 12).ToUpperInvariant(),
                Message = declined ? "Your card was declined." : "Approved"
            };
        }

        public PaymentResult Refund(string providerRef, decimal amount)
        {
            Audit(string.Format("REFUND providerRef={0} amount={1:0.00}", providerRef, amount));
            return new PaymentResult { Success = true, ProviderRef = providerRef, Message = "Refunded" };
        }

        private void Audit(string message)
        {
            string path = _paths.Resolve("~/App_Data/payments.log");
            Directory.CreateDirectory(Path.GetDirectoryName(path));
            File.AppendAllText(path, DateTime.UtcNow.ToString("o") + " ip=" + (_client.IpAddress ?? "-") + " " + message + Environment.NewLine);
        }
    }
}
