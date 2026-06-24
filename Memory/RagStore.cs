using System.Text;

namespace MiniCursorAgent.Memory;

public sealed class RagStore
{
    private record Chunk(string Text, string Source, int StartLine, Dictionary<string, int> TermFreq);

    private readonly List<Chunk> _chunks = new();
    private readonly object _lock = new();

    public int ChunkCount
    {
        get { lock (_lock) { return _chunks.Count; } }
    }

    public void Index(string text, string source)
    {
        if (string.IsNullOrWhiteSpace(text)) return;

        lock (_lock)
        {
            _chunks.RemoveAll(c => string.Equals(c.Source, source, StringComparison.OrdinalIgnoreCase));

            var lines = text.Split('\n');
            const int chunkSize = 25;

            for (var i = 0; i < lines.Length; i += chunkSize)
            {
                var chunkLines = lines.Skip(i).Take(chunkSize).ToArray();
                var chunkText = string.Join('\n', chunkLines);
                if (string.IsNullOrWhiteSpace(chunkText)) continue;

                var tf = Tokenize(chunkText);
                _chunks.Add(new Chunk(chunkText, source, i + 1, tf));
            }
        }
    }

    public List<(string Text, string Source, int StartLine, double Score)> Search(string query, int topK = 3)
    {
        if (string.IsNullOrWhiteSpace(query)) return new();

        lock (_lock)
        {
            if (_chunks.Count == 0) return new();

            var queryTerms = Tokenize(query);
            var scored = _chunks
                .Select(c => (c, score: CosineSimilarity(queryTerms, c.TermFreq)))
                .Where(x => x.score > 0)
                .OrderByDescending(x => x.score)
                .Take(topK)
                .ToList();

            return scored
                .Select(x => (x.c.Text, x.c.Source, x.c.StartLine, x.score))
                .ToList();
        }
    }

    private static Dictionary<string, int> Tokenize(string text)
    {
        var terms = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        var current = new StringBuilder();

        foreach (var ch in text)
        {
            if (char.IsLetterOrDigit(ch) || ch == '_')
            {
                current.Append(ch);
            }
            else
            {
                if (current.Length >= 2)
                {
                    var word = current.ToString().ToLowerInvariant();
                    terms.TryGetValue(word, out var cnt);
                    terms[word] = cnt + 1;
                }
                current.Clear();
            }
        }

        if (current.Length >= 2)
        {
            var word = current.ToString().ToLowerInvariant();
            terms.TryGetValue(word, out var cnt);
            terms[word] = cnt + 1;
        }

        return terms;
    }

    private static double CosineSimilarity(Dictionary<string, int> a, Dictionary<string, int> b)
    {
        var dot = 0.0;
        var normA = 0.0;
        var normB = 0.0;

        foreach (var (term, freqA) in a)
        {
            normA += (double)freqA * freqA;
            if (b.TryGetValue(term, out var freqB))
                dot += (double)freqA * freqB;
        }

        foreach (var freqB in b.Values)
            normB += (double)freqB * freqB;

        if (normA == 0 || normB == 0) return 0;
        return dot / (Math.Sqrt(normA) * Math.Sqrt(normB));
    }
}
