using System.ComponentModel.DataAnnotations;
using System.Linq;

namespace AutoPartsWeb.Services
{
    public static class ValidationRules
    {
        private static readonly EmailAddressAttribute EmailValidator = new();

        public static bool IsValidEmail(string? email)
        {
            if (string.IsNullOrWhiteSpace(email))
                return false;

            return EmailValidator.IsValid(email);
        }

        public static bool TryValidatePassword(string? password, out string error)
        {
            if (string.IsNullOrWhiteSpace(password))
            {
                error = "Sifre zorunlu.";
                return false;
            }

            if (password.Length < 8)
            {
                error = "Sifre en az 8 karakter olmali.";
                return false;
            }

            if (!password.Any(char.IsUpper))
            {
                error = "Sifre en az bir buyuk harf icermeli.";
                return false;
            }

            if (!password.Any(char.IsLower))
            {
                error = "Sifre en az bir kucuk harf icermeli.";
                return false;
            }

            if (!password.Any(char.IsDigit))
            {
                error = "Sifre en az bir rakam icermeli.";
                return false;
            }

            error = "";
            return true;
        }
    }
}
