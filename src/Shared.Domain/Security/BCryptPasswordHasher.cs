namespace ConfReg.Domain.Security
{
    /// <summary>
    /// BASELINE-PROBLEM #7, fixed: salted, deliberately slow, one-way. A leaked table can no longer be turned back
    /// into passwords with a key, and identical passwords no longer produce identical values.
    /// </summary>
    public class BCryptPasswordHasher : IPasswordHasher
    {
        // Work factor 11 is about 100 ms per check on current hardware: slow enough to hurt an attacker,
        // unnoticeable to an admin logging in. Raise it as hardware improves; old hashes keep verifying.
        private const int WorkFactor = 11;

        public string Hash(string password) { return BCrypt.Net.BCrypt.HashPassword(password, WorkFactor); }

        public bool Verify(string password, string hash)
        {
            try { return BCrypt.Net.BCrypt.Verify(password, hash); }
            catch (BCrypt.Net.SaltParseException) { return false; }
        }

        // BCrypt strings are "$2a$", "$2b$" or "$2y$"; legacy AES output is base64 and can never start with '$'.
        public bool IsCurrentFormat(string stored) { return stored != null && stored.StartsWith("$2"); }
    }
}
