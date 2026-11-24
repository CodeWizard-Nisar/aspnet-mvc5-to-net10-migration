using System;

namespace ConfReg.Domain.Rules
{
    public static class TicketRules
    {
        /// <summary>Student tickets require an .edu address. Deliberately strict: "ac.uk" and friends are rejected.</summary>
        public static bool IsEduEmail(string email)
        {
            if (string.IsNullOrWhiteSpace(email)) return false;
            int at = email.LastIndexOf('@');
            if (at < 1 || at == email.Length - 1) return false;
            string domain = email.Substring(at + 1).Trim();
            return domain.EndsWith(".edu", StringComparison.OrdinalIgnoreCase);
        }
    }
}
