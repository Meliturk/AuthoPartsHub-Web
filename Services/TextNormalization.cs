using System.Text;
using AutoPartsWeb.Data;

namespace AutoPartsWeb.Services
{
    public static class TextNormalization
    {
        private static readonly HashSet<char> MojibakeMarkers = new HashSet<char>
        {
            '\u00C3', '\u00C2', '\u00C5', '\u00C4', '\u00D0', '\u00DE', '\u00DD', '\u00D7',
            '\u0192', '\u2020', '\u2021', '\u2122', '\u0153', '\u017E', '\u0178', '\u0160',
            '\u017D', '\u0152', '\u00F8', '\u00A2', '\u00A3', '\u00A4', '\u00A7', '\u00A8',
            '\u00B4', '\u00B8', '\u00BF', '\u00A1', '\u00AF', '\u00AC'
        };

        private static readonly Encoding[] LegacyEncodings = new[]
        {
            Encoding.GetEncoding(1252),
            Encoding.GetEncoding(1254),
            Encoding.GetEncoding(28599),
            Encoding.GetEncoding(857)
        };

        public static string? Normalize(string? value)
        {
            if (string.IsNullOrWhiteSpace(value))
                return value;

            if (!LooksMojibake(value))
                return value;

            var current = value;
            for (var pass = 0; pass < 2; pass++)
            {
                var candidate = BestCandidate(current);
                if (candidate == current)
                    break;

                current = candidate;
                if (!LooksMojibake(current))
                    break;
            }

            return current;
        }

        public static int NormalizeDatabase(ApplicationDbContext db)
        {
            var changed = 0;

            foreach (var part in db.Parts)
            {
                changed += FixRequired(part.Name, v => part.Name = v);
                changed += FixRequired(part.Brand, v => part.Brand = v);
                changed += FixRequired(part.Category, v => part.Category = v);
                changed += FixOptional(part.Description, v => part.Description = v);
                changed += FixOptional(part.Condition, v => part.Condition = v);
            }

            foreach (var vehicle in db.Vehicles)
            {
                changed += FixRequired(vehicle.Brand, v => vehicle.Brand = v);
                changed += FixRequired(vehicle.Model, v => vehicle.Model = v);
                changed += FixOptional(vehicle.Engine, v => vehicle.Engine = v);
            }

            foreach (var user in db.AppUsers)
            {
                changed += FixRequired(user.FullName, v => user.FullName = v);
            }

            foreach (var app in db.SellerApplications)
            {
                changed += FixRequired(app.CompanyName, v => app.CompanyName = v);
                changed += FixRequired(app.ContactName, v => app.ContactName = v);
                changed += FixRequired(app.Address, v => app.Address = v);
                changed += FixOptional(app.Note, v => app.Note = v);
                changed += FixOptional(app.TaxNumber, v => app.TaxNumber = v);
            }

            foreach (var order in db.Orders)
            {
                changed += FixRequired(order.CustomerName, v => order.CustomerName = v);
                changed += FixRequired(order.Address, v => order.Address = v);
                changed += FixOptional(order.City, v => order.City = v);
            }

            foreach (var msg in db.ContactMessages)
            {
                changed += FixRequired(msg.Name, v => msg.Name = v);
                changed += FixRequired(msg.Message, v => msg.Message = v);
            }

            foreach (var q in db.ProductQuestions)
            {
                changed += FixRequired(q.Question, v => q.Question = v);
                changed += FixOptional(q.Answer, v => q.Answer = v);
            }

            foreach (var review in db.ProductReviews)
            {
                changed += FixOptional(review.Comment, v => review.Comment = v);
            }

            if (changed > 0)
                db.SaveChanges();

            return changed;
        }

        private static string BestCandidate(string value)
        {
            var best = value;
            var bestScore = Score(value);

            foreach (var legacy in LegacyEncodings)
            {
                var candidate = Reencode(value, legacy);
                var score = Score(candidate);
                if (score < bestScore)
                {
                    bestScore = score;
                    best = candidate;
                }
            }

            return best;
        }

        private static string Reencode(string value, Encoding legacy)
        {
            var bytes = legacy.GetBytes(value);
            return Encoding.UTF8.GetString(bytes);
        }

        private static bool LooksMojibake(string value)
        {
            foreach (var ch in value)
            {
                if (ch == '\uFFFD')
                    return true;

                if (ch <= '\u001F' || (ch >= '\u007F' && ch <= '\u009F'))
                    return true;

                if (MojibakeMarkers.Contains(ch))
                    return true;
            }

            return false;
        }

        private static int Score(string value)
        {
            var bad = 0;
            foreach (var ch in value)
            {
                if (ch == '\uFFFD')
                    bad += 5;
                else if (ch <= '\u001F' || (ch >= '\u007F' && ch <= '\u009F'))
                    bad += 3;
                else if (MojibakeMarkers.Contains(ch))
                    bad += 2;
            }

            return bad;
        }

        private static int FixOptional(string? value, Action<string?> setter)
        {
            var normalized = Normalize(value);
            if (!string.Equals(value, normalized, StringComparison.Ordinal))
            {
                setter(normalized);
                return 1;
            }

            return 0;
        }

        private static int FixRequired(string value, Action<string> setter)
        {
            var normalized = Normalize(value) ?? value;
            if (!string.Equals(value, normalized, StringComparison.Ordinal))
            {
                setter(normalized);
                return 1;
            }

            return 0;
        }
    }
}
