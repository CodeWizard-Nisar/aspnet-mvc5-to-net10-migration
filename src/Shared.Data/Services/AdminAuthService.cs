using System.Linq;
using ConfReg.Data.Infrastructure;
using ConfReg.Domain.Entities;
using ConfReg.Domain.Security;

namespace ConfReg.Data.Services
{
    public class AdminAuthService
    {
        private readonly IConfRegContextFactory _db;
        private readonly IPasswordHasher _hasher;
        private readonly LegacyPasswordCipher _legacy;

        public AdminAuthService(IConfRegContextFactory db, IPasswordHasher hasher, LegacyPasswordCipher legacy)
        {
            _db = db;
            _hasher = hasher;
            _legacy = legacy;
        }

        public AdminUser Validate(string email, string password)
        {
            if (string.IsNullOrWhiteSpace(email) || password == null) return null;

            using (var ctx = _db.Create())
            {
                var user = ctx.AdminUsers.FirstOrDefault(u => u.Email == email);
                if (user == null) return null;

                // Current format: a normal BCrypt check.
                if (_hasher.IsCurrentFormat(user.PasswordHash))
                    return _hasher.Verify(password, user.PasswordHash) ? user : null;

                // BASELINE-PROBLEM #7, migration path: a row written before BCrypt. Check it the old way ONE last
                // time and, if the password is right, replace the stored value with a BCrypt hash straight away.
                // The plain-text password is only ever available at this moment, which is why the upgrade happens
                // here, on login, and cannot be done as a batch job.
                if (!_legacy.Matches(user.PasswordHash, password)) return null;

                user.PasswordHash = _hasher.Hash(password);
                ctx.SaveChanges();
                return user;
            }
        }
    }
}
