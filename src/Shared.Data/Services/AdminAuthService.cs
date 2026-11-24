using System;
using System.Linq;
using ConfReg.Domain.Entities;
using ConfReg.Domain.Services;

namespace ConfReg.Data.Services
{
    public class AdminAuthService
    {
        public AdminUser Validate(string email, string password)
        {
            if (string.IsNullOrWhiteSpace(email) || password == null) return null;

            using (var ctx = new ConfRegContext())
            {
                var user = ctx.AdminUsers.FirstOrDefault(u => u.Email == email);
                if (user == null) return null;

                // BASELINE-PROBLEM #7: "verify" by DECRYPTING the stored value and comparing plain text.
                // Also a plain string == (not constant time).
                return PasswordCrypto.Decrypt(user.PasswordHash) == password ? user : null;
            }
        }
    }
}
