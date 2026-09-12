namespace SapB1.ItemImport.Core.Csv;

/// <summary>
/// Infers the field delimiter from a header line. Item exports arrive as comma files from most
/// systems but as semicolon files from Excel on comma-decimal locales, so guessing beats forcing
/// every operator to pass a flag.
/// </summary>
public static class DelimiterDetector
{
    private static readonly char[] Candidates = { ',', ';', '\t', '|' };

    public const char Fallback = ',';

    /// <summary>Returns the most frequent candidate delimiter outside quoted sections, or <see cref="Fallback"/>.</summary>
    public static char Detect(string? headerLine, char quote = '"')
    {
        if (string.IsNullOrEmpty(headerLine))
        {
            return Fallback;
        }

        var counts = new int[Candidates.Length];
        var insideQuotes = false;

        foreach (var current in headerLine)
        {
            if (current == quote)
            {
                insideQuotes = !insideQuotes;
                continue;
            }

            if (insideQuotes)
            {
                continue;
            }

            for (var i = 0; i < Candidates.Length; i++)
            {
                if (current == Candidates[i])
                {
                    counts[i]++;
                }
            }
        }

        var bestIndex = -1;
        var bestCount = 0;
        for (var i = 0; i < Candidates.Length; i++)
        {
            if (counts[i] > bestCount)
            {
                bestCount = counts[i];
                bestIndex = i;
            }
        }

        return bestIndex < 0 ? Fallback : Candidates[bestIndex];
    }
}
