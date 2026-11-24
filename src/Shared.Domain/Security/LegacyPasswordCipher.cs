using System;
using System.Security.Cryptography;
using System.Text;
using ConfReg.Domain.Abstractions;

namespace ConfReg.Domain.Security
{
    /// <summary>
    /// The Phase 1 reversible password "hashing" (BASELINE-PROBLEM #7), kept ONLY so existing rows can be checked once.
    ///
    /// Why it still exists: we cannot bulk-convert, because converting needs the plain-text password, which is exactly
    /// what we are not supposed to have. So AdminAuthService verifies a legacy row with this class and, on success,
    /// immediately re-hashes the password with BCrypt and overwrites the row. Each admin is upgraded the next time they
    /// log in. When no row starts without "$2" any more, delete this class and the two Crypto settings.
    /// </summary>
    public class LegacyPasswordCipher
    {
        private readonly ISettings _settings;
        public LegacyPasswordCipher(ISettings settings) { _settings = settings; }

        public string Encrypt(string plain)
        {
            using (var aes = CreateAes())
            using (var enc = aes.CreateEncryptor())
            {
                byte[] bytes = Encoding.UTF8.GetBytes(plain);
                return Convert.ToBase64String(enc.TransformFinalBlock(bytes, 0, bytes.Length));
            }
        }

        /// <summary>True if <paramref name="stored"/> decrypts to <paramref name="password"/>. Never throws on garbage.</summary>
        public bool Matches(string stored, string password)
        {
            try
            {
                using (var aes = CreateAes())
                using (var dec = aes.CreateDecryptor())
                {
                    byte[] cipher = Convert.FromBase64String(stored);
                    byte[] plain = dec.TransformFinalBlock(cipher, 0, cipher.Length);
                    return FixedTimeEquals(plain, Encoding.UTF8.GetBytes(password));
                }
            }
            catch (FormatException) { return false; }
            catch (CryptographicException) { return false; }
        }

        // Phase 1 compared with a plain string ==, which stops at the first different character.
        private static bool FixedTimeEquals(byte[] a, byte[] b)
        {
            int diff = a.Length ^ b.Length;
            for (int i = 0; i < a.Length && i < b.Length; i++) diff |= a[i] ^ b[i];
            return diff == 0;
        }

        private Aes CreateAes()
        {
            var aes = Aes.Create();
            aes.Key = Convert.FromBase64String(_settings.LegacyCryptoKey);
            aes.IV = Convert.FromBase64String(_settings.LegacyCryptoIv);
            return aes;
        }
    }
}
