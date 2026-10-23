using System.Text;
using Robust.Shared.Random;

namespace Content.Server._VG.Speech;

/// <summary>
///     vg13's speech mangling procs (mob_helpers.dm).
/// </summary>
public static class VgSpeech
{
    private const string StutterLetters = "bcdfghjklmnpqrstvwxyz";

    /// <summary>
    ///     vg13 slur(): drunken letters and random case.
    /// </summary>
    public static string Slur(string phrase, IRobustRandom random)
    {
        var sb = new StringBuilder(phrase.Length * 2);
        foreach (var character in phrase)
        {
            var letter = character.ToString();
            if (random.Next(1, 4) == 3)
            {
                letter = char.ToLowerInvariant(character) switch
                {
                    'o' => "u",
                    's' => "ch",
                    'a' => "ah",
                    'c' => "k",
                    _ => letter,
                };
            }

            switch (random.Next(1, 16))
            {
                case 1 or 3 or 5 or 8:
                    letter = letter.ToLowerInvariant();
                    break;
                case 2 or 4 or 6 or 15:
                    letter = letter.ToUpperInvariant();
                    break;
                case 7:
                    letter += "'";
                    break;
            }

            sb.Append(letter);
        }

        return sb.ToString();
    }

    /// <summary>
    ///     vg13 stutter(): consonants repeat, b-b-but sometimes go missing.
    /// </summary>
    public static string Stutter(string phrase, IRobustRandom random)
    {
        var sb = new StringBuilder(phrase.Length * 2);
        foreach (var character in phrase)
        {
            var letter = character.ToString();
            if (random.Prob(0.8f) && StutterLetters.Contains(char.ToLowerInvariant(character)))
            {
                if (random.Prob(0.1f))
                    letter = $"{letter}-{letter}-{letter}-{letter}";
                else if (random.Prob(0.2f))
                    letter = $"{letter}-{letter}-{letter}";
                else if (random.Prob(0.05f))
                    letter = string.Empty;
                else
                    letter = $"{letter}-{letter}";
            }

            sb.Append(letter);
        }

        return sb.ToString();
    }
}
