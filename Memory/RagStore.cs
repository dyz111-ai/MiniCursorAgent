using System.Text;

namespace MiniCursorAgent.Memory;

public sealed class RagStore
{
    private record Chunk(string Text, string EntryId, string EntryTitle, Dictionary<string, int> TermFreq);

    private readonly List<Chunk> _chunks = new();

    public int Count => _chunks.Count;

    public RagStore()
    {
        foreach (var entry in KnowledgeBase.Entries)
        {
            var fullText = entry.Title + "\n" + entry.Content;
            _chunks.Add(new Chunk(entry.Content, entry.Id, entry.Title, Tokenize(fullText)));
        }
    }

    public List<(string Content, string Title, double Score)> Search(string query, int topK = 3)
    {
        if (string.IsNullOrWhiteSpace(query) || _chunks.Count == 0)
            return new();

        var queryTerms = Tokenize(query);

        return _chunks
            .Select(c => (c, score: CosineSimilarity(queryTerms, c.TermFreq)))
            .Where(x => x.score > 0.01)
            .OrderByDescending(x => x.score)
            .Take(topK)
            .Select(x => (x.c.Text, x.c.EntryTitle, x.score))
            .ToList();
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

        return normA == 0 || normB == 0 ? 0 : dot / (Math.Sqrt(normA) * Math.Sqrt(normB));
    }
}
