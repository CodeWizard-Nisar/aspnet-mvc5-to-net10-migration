using System;

namespace ConfReg.Domain.Rules
{
    public static class ReferenceCodes
    {
        // No 0/O/1/I: these get read aloud and typed from a confirmation email.
        private const string Alphabet = "ABCDEFGHJKLMNPQRSTUVWXYZ23456789";
        private static readonly Random Rng = new Random();
        private static readonly object Gate = new object();

        public static string Next() { lock (Gate) { return Build(Rng); } }

        /// <summary>Seeded variant so the seed data is identical on every machine.</summary>
        public static string Build(Random rng)
        {
            var chars = new char[8];
            for (int i = 0; i < chars.Length; i++) chars[i] = Alphabet[rng.Next(Alphabet.Length)];
            return "CR-" + new string(chars);
        }
    }
}
