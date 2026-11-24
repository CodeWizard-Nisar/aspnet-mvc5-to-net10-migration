using System;
using System.Configuration;
using System.IO;
using System.Security.Cryptography;
using System.Text;

namespace ConfReg.Domain.Services
{
    /// <summary>
    /// BASELINE-PROBLEM #7: admin passwords are ENCRYPTED, not hashed.
    ///
    /// Why this is wrong, not just unfashionable:
    ///  - Anyone with the key (it is in Web.config, BASELINE-PROBLEM #4) recovers every password.
    ///  - One fixed IV for every row, so identical passwords produce identical ciphertext and
    ///    you can see who shares a password without any key at all.
    ///  - Nothing makes it slow, so a leaked table can be attacked at full speed.
    /// Fixed in Phase 3: BCrypt, upgrading each row on that user's next successful login
    /// (we cannot bulk-convert: the plain text is exactly what we are not supposed to have).
    /// </summary>
    public static class PasswordCrypto
    {
        public static string Encrypt(string plain)
        {
            using (var aes = CreateAes())
            using (var enc = aes.CreateEncryptor())
            {
                byte[] bytes = Encoding.UTF8.GetBytes(plain);
                return Convert.ToBase64String(enc.TransformFinalBlock(bytes, 0, bytes.Length));
            }
        }

        public static string Decrypt(string cipher)
        {
            using (var aes = CreateAes())
            using (var dec = aes.CreateDecryptor())
            {
                byte[] bytes = Convert.FromBase64String(cipher);
                return Encoding.UTF8.GetString(dec.TransformFinalBlock(bytes, 0, bytes.Length));
            }
        }

        private static Aes CreateAes()
        {
            var aes = Aes.Create();
            aes.Key = Convert.FromBase64String(ConfigurationManager.AppSettings["Crypto.Key"]);
            aes.IV = Convert.FromBase64String(ConfigurationManager.AppSettings["Crypto.IV"]);
            return aes;
        }
    }
}
