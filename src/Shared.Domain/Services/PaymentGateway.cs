using System;
using System.Configuration;
using System.IO;
using System.Web;

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
    /// </summary>
    public class PaymentGateway
    {
        public PaymentResult Charge(decimal amount, string cardNumber, string referenceCode)
        {
            // BASELINE-PROBLEM #3 and #4: the API key is read inline and sits in plain text in Web.config.
            string apiKey = ConfigurationManager.AppSettings["Payment.ApiKey"];
            if (string.IsNullOrEmpty(apiKey)) throw new InvalidOperationException("Payment.ApiKey is not configured.");

            string digits = new string((cardNumber ?? "").ToCharArray()).Replace(" ", "");
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

        private static void Audit(string message)
        {
            // BASELINE-PROBLEM #1 (again): a payment gateway wrapper that needs a live web request,
            // for two things that have nothing to do with payments:
            //   Request.UserHostAddress   -> becomes IClientInfo in Phase 3
            //   Server.MapPath("~/...")   -> becomes IAppPaths   in Phase 3
            var ctx = HttpContext.Current;
            string ip = ctx.Request.UserHostAddress;
            string path = ctx.Server.MapPath("~/App_Data/payments.log");
            Directory.CreateDirectory(Path.GetDirectoryName(path));
            File.AppendAllText(path, DateTime.UtcNow.ToString("o") + " ip=" + ip + " " + message + Environment.NewLine);
        }
    }
}
