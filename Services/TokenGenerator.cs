using System.Security.Cryptography;

namespace AutoPartsWeb.Services
{
    public static class TokenGenerator
    {
        public static string CreateToken(int byteLength = 32)
        {
            return Convert.ToHexString(RandomNumberGenerator.GetBytes(byteLength));
        }
    }
}
