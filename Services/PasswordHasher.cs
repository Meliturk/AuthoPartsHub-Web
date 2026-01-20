using System.Security.Cryptography;
using System.Text;

namespace AutoPartsWeb.Services
{
    public static class PasswordHasher
    {
        // Daha güçlü SHA-512 tabanlı hash
        public static string Hash(string input)
        {
            using var sha = SHA512.Create();
            var bytes = Encoding.UTF8.GetBytes(input ?? string.Empty);
            var hash = sha.ComputeHash(bytes);
            return Convert.ToHexString(hash);
        }

        public static bool Verify(string input, string hash) =>
            string.Equals(Hash(input), hash, StringComparison.OrdinalIgnoreCase);
    }
}
