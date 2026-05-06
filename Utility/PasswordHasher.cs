using System;
using System.Security.Cryptography;

namespace TabletCheckIn.Utility
{
    public static class PasswordHasher
    {
        private const int SaltSize = 16;
        private const int HashSize = 32;
        private const int Iterations = 100000;

        public static string Hash(string password)
        {
            byte[] salt = new byte[SaltSize];
            using (var rng = new RNGCryptoServiceProvider())
                rng.GetBytes(salt);

            using (var pbkdf2 = new Rfc2898DeriveBytes(password, salt, Iterations))
            {
                byte[] hash = pbkdf2.GetBytes(HashSize);
                byte[] combined = new byte[SaltSize + HashSize];
                Buffer.BlockCopy(salt, 0, combined, 0, SaltSize);
                Buffer.BlockCopy(hash, 0, combined, SaltSize, HashSize);
                return Convert.ToBase64String(combined);
            }
        }

        public static bool Verify(string password, string storedValue)
        {
            if (IsHashed(storedValue))
                return VerifyHash(password, storedValue);
            // Migration path: compare plaintext until user's password is upgraded
            return string.Equals(password, storedValue, StringComparison.Ordinal);
        }

        public static bool IsHashed(string value)
        {
            try
            {
                byte[] b = Convert.FromBase64String(value);
                return b.Length == SaltSize + HashSize;
            }
            catch { return false; }
        }

        private static bool VerifyHash(string password, string storedHash)
        {
            try
            {
                byte[] combined = Convert.FromBase64String(storedHash);
                byte[] salt = new byte[SaltSize];
                Buffer.BlockCopy(combined, 0, salt, 0, SaltSize);

                using (var pbkdf2 = new Rfc2898DeriveBytes(password, salt, Iterations))
                {
                    byte[] hash = pbkdf2.GetBytes(HashSize);
                    for (int i = 0; i < HashSize; i++)
                        if (combined[SaltSize + i] != hash[i]) return false;
                    return true;
                }
            }
            catch { return false; }
        }
    }
}
