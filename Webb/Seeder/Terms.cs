using System.Text.RegularExpressions;

namespace Webb.Seeder
{
    // The stage engine came from the AUFPV platform and names things its way:
    // a "Redemption" side, "A Main", "heats". FPVTrackside calls those the
    // Losers bracket, bracket A and races, so every engine label and message
    // passes through here on its way to a person.
    public static class Terms
    {
        private static readonly Regex main = new Regex(@"\b(?:the )?([A-Z]) Main\b");
        private static readonly Regex heat = new Regex(@"\b([Hh])eat(s?)\b");
        private static readonly Regex bracket = new Regex(@"(?<!(?:inners|osers|emption) )\b([Bb])racket(s?)\b");

        public static string Trackside(string text)
        {
            if (string.IsNullOrEmpty(text))
                return text;
            string t = text.Replace("Redemption", "Losers").Replace("redemption", "losers");
            // Engine messages say "bracket" for the whole thing (a stage here);
            // "winners bracket"/"losers bracket" keep the word.
            t = bracket.Replace(t, m => (m.Groups[1].Value == "B" ? "Stage" : "stage") + m.Groups[2].Value);
            t = main.Replace(t, m => "Bracket " + m.Groups[1].Value);
            t = heat.Replace(t, m => (m.Groups[1].Value == "H" ? "Race" : "race") + m.Groups[2].Value);
            return t;
        }
    }
}
