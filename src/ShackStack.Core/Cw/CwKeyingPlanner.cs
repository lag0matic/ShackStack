namespace ShackStack.Core.Cw;

public static class CwKeyingPlanner
{
    private static readonly IReadOnlyDictionary<char, string> Morse = new Dictionary<char, string>
    {
        ['A'] = ".-",
        ['B'] = "-...",
        ['C'] = "-.-.",
        ['D'] = "-..",
        ['E'] = ".",
        ['F'] = "..-.",
        ['G'] = "--.",
        ['H'] = "....",
        ['I'] = "..",
        ['J'] = ".---",
        ['K'] = "-.-",
        ['L'] = ".-..",
        ['M'] = "--",
        ['N'] = "-.",
        ['O'] = "---",
        ['P'] = ".--.",
        ['Q'] = "--.-",
        ['R'] = ".-.",
        ['S'] = "...",
        ['T'] = "-",
        ['U'] = "..-",
        ['V'] = "...-",
        ['W'] = ".--",
        ['X'] = "-..-",
        ['Y'] = "-.--",
        ['Z'] = "--..",
        ['1'] = ".----",
        ['2'] = "..---",
        ['3'] = "...--",
        ['4'] = "....-",
        ['5'] = ".....",
        ['6'] = "-....",
        ['7'] = "--...",
        ['8'] = "---..",
        ['9'] = "----.",
        ['0'] = "-----",
        ['.'] = ".-.-.-",
        [','] = "--..--",
        ['?'] = "..--..",
        ['/'] = "-..-.",
        ['='] = "-...-",
        ['+'] = ".-.-.",
        ['-'] = "-....-",
    };

    public static IReadOnlyList<CwKeyingStep> BuildPlan(string text, int wpm)
    {
        var sanitized = (text ?? string.Empty).Trim().ToUpperInvariant();
        if (string.IsNullOrWhiteSpace(sanitized))
        {
            return [];
        }

        var ditMs = Math.Clamp(1200 / Math.Max(5, Math.Min(60, wpm)), 20, 240);
        var steps = new List<CwKeyingStep>();
        var words = sanitized
            .Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

        for (var wordIndex = 0; wordIndex < words.Length; wordIndex++)
        {
            var word = words[wordIndex];
            var encodedChars = word
                .Select(ch => Morse.TryGetValue(ch, out var pattern) ? pattern : null)
                .Where(pattern => !string.IsNullOrWhiteSpace(pattern))
                .ToArray();

            for (var charIndex = 0; charIndex < encodedChars.Length; charIndex++)
            {
                var pattern = encodedChars[charIndex]!;
                for (var elementIndex = 0; elementIndex < pattern.Length; elementIndex++)
                {
                    var isDah = pattern[elementIndex] == '-';
                    steps.Add(new CwKeyingStep(true, isDah ? ditMs * 3 : ditMs));

                    var isLastElement = elementIndex == pattern.Length - 1;
                    if (!isLastElement)
                    {
                        steps.Add(new CwKeyingStep(false, ditMs));
                    }
                }

                var isLastCharInWord = charIndex == encodedChars.Length - 1;
                if (!isLastCharInWord)
                {
                    steps.Add(new CwKeyingStep(false, ditMs * 3));
                }
            }

            var isLastWord = wordIndex == words.Length - 1;
            if (!isLastWord)
            {
                steps.Add(new CwKeyingStep(false, ditMs * 7));
            }
        }

        return steps;
    }
}
