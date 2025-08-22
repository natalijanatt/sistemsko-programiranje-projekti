using Microsoft.ML;
using Microsoft.ML.Data;
using Microsoft.ML.Transforms.Text;
using projekat_tri;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace projekat_tri
{
    public class TopicModeler
    {
        private readonly MLContext ml;

        public TopicModeler(int? seed = 1) => ml = new MLContext(seed: seed);

        public string AnalyzeTopics(IEnumerable<CommentData> comments, int numberOfTopics = 5, int topWordsPerTopic = 6, int examplesPerTopic = 2)
        {
            var list = comments
                .Where(c => !string.IsNullOrWhiteSpace(c.Text))
                .Select(c => new CommentData { Text = c.Text.Trim() })
                .ToList();

            if (list.Count == 0) return "No comments.";

            var data = ml.Data.LoadFromEnumerable(list);

            var pipeline =
                        ml.Transforms.Text.NormalizeText(
                                outputColumnName: "TextNorm",
                                inputColumnName: nameof(CommentData.Text),
                                caseMode: TextNormalizingEstimator.CaseMode.Lower,
                                keepDiacritics: false,
                                keepPunctuations: false,
                                keepNumbers: false)
                        .Append(ml.Transforms.Text.TokenizeIntoWords("Tokens", "TextNorm"))
                        .Append(ml.Transforms.Text.RemoveDefaultStopWords("TokensClean", "Tokens"))
                        .Append(ml.Transforms.Conversion.MapValueToKey(
                                outputColumnName: "TokensKeyed",
                                inputColumnName: "TokensClean",
                                keyOrdinality: Microsoft.ML.Transforms.ValueToKeyMappingEstimator.KeyOrdinality.ByOccurrence,
                                addKeyValueAnnotationsAsText: true))
                        .Append(ml.Transforms.Text.ProduceNgrams(
                                outputColumnName: "Ngrams",
                                inputColumnName: "TokensKeyed",
                                ngramLength: 1,
                                useAllLengths: true))
                        .Append(ml.Transforms.Text.LatentDirichletAllocation(
                                outputColumnName: "TopicVector",
                                inputColumnName: "Ngrams",
                                numberOfTopics: numberOfTopics,
                                maximumNumberOfIterations: 100));


            var model = pipeline.Fit(data);
            var transformed = model.Transform(data);

            var rows = ml.Data.CreateEnumerable<LdaRow>(transformed, reuseRowObject: false).ToList();

            var projected = rows.Select((r, idx) =>
            {
                var vec = r.TopicVector ?? Array.Empty<float>();
                int bestTopic = 0;
                float bestScore = float.MinValue;
                for (int t = 0; t < vec.Length; t++)
                    if (vec[t] > bestScore) { bestScore = vec[t]; bestTopic = t; }

                return new { Topic = bestTopic, Text = list[idx].Text, Dist = vec };
            }).ToList();

            static IEnumerable<string> SimpleTokens(string text, HashSet<string> stop)
            {
                if (string.IsNullOrEmpty(text)) yield break;
                var toks = text
                    .ToLowerInvariant()
                    .Split(new[] { ' ', '\t', '\r', '\n', '.', ',', ';', ':', '!', '?', '"', '\'', '(', ')', '[', ']', '{', '}', '/', '\\', '-', '_', '#', '@', '|', '*', '`', '~', '<', '>', '=' },
                           StringSplitOptions.RemoveEmptyEntries);

                foreach (var tok in toks)
                {
                    var w = tok.Trim();
                    if (w.Length <= 2) continue;
                    if (stop.Contains(w)) continue;
                    yield return w;
                }
            }

            var stopwords = new HashSet<string>(new[]
            {
                "the","a","an","and","or","of","in","on","to","is","are","was","were","it","for","with",
                "i","you","we","they","this","that","as","be","by","from","at","if","not","can","could",
                "should","would","have","has","had","but","so","do","does","did","your","our","their",
                "there","here","then","than","also","just","any","some","more","most","least","many"
            });

            var sb = new StringBuilder();
            sb.AppendLine($"Detected {numberOfTopics} topics across {list.Count} comments");
            sb.AppendLine(new string('=', 64));

            for (int t = 0; t < numberOfTopics; t++)
            {
                var group = projected.Where(p => p.Topic == t).ToList();
                if (group.Count == 0)
                {
                    sb.AppendLine($"\n=== Topic {t + 1} (0 comments) ===");
                    continue;
                }

                var freq = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
                foreach (var item in group)
                    foreach (var w in SimpleTokens(item.Text, stopwords))
                        freq[w] = (freq.TryGetValue(w, out var c) ? c + 1 : 1);

                var topWords = freq.OrderByDescending(kv => kv.Value)
                                   .Take(topWordsPerTopic)
                                   .Select(kv => kv.Key)
                                   .ToArray();

                sb.AppendLine($"\n=== Topic {t + 1} ({group.Count} comments) ===");
                sb.AppendLine("Top words: " + (topWords.Length > 0 ? string.Join(", ", topWords) : "(n/a)"));
                sb.AppendLine("Bar: " + new string('*', Math.Min(group.Count, 60)));

                foreach (var ex in group.Take(examplesPerTopic))
                {
                    var preview = ex.Text.Replace("\r", " ").Replace("\n", " ");
                    if (preview.Length > 500) preview = preview.Substring(0, 500) + "…";
                    sb.AppendLine($" • {preview}");
                }
            }

            return sb.ToString();
        }

        private class LdaRow
        {
            [VectorType]
            public float[] TopicVector { get; set; } = Array.Empty<float>();
        }
    }
}
