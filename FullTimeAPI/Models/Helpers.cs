using System.Text.RegularExpressions;

namespace FullTimeAPI.Models
{
    public class Helpers
    {
        public static string NormalizeText(string input)
        {
            if (string.IsNullOrEmpty(input))
                return string.Empty;
            return Regex.Replace(input, @"\s+", " ").Trim();
        }
    }
}
