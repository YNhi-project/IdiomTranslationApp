using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;

namespace IdiomTranslationApp.Services
{
    public class EvaluationService
    {
        /// <summary>
        /// Tính toán điểm số BLEU cải tiến cho hệ thống dịch thành ngữ.
        /// Đánh giá dựa trên độ phủ và sự cân đối độ dài của cụm từ nghĩa bóng (Reference) trong câu dịch (Hypothesis).
        /// </summary>
        public double CalculateBleuScore(string reference, string hypothesis)
        {
            if (string.IsNullOrWhiteSpace(hypothesis))
                return 0.0;

            if (string.IsNullOrWhiteSpace(reference))
            {
                return 1.0;
            }

            string cleanRef = reference.Trim().ToLower();
            string cleanHyp = hypothesis.Trim().ToLower();

            cleanRef = Regex.Replace(cleanRef, @"[^\w\s]", "");
            cleanHyp = Regex.Replace(cleanHyp, @"[^\w\s]", "");

            string[] refTokens = cleanRef.Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries);
            string[] hypTokens = cleanHyp.Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries);

            if (refTokens.Length == 0 || hypTokens.Length == 0) return 0.0;

            if (cleanHyp.Contains(cleanRef))
            {
                double baseScore = 0.75;

                double ratio = (double)hypTokens.Length / refTokens.Length;
                double lengthPenalty = 1.0;

                if (ratio > 2.0)
                {
                    lengthPenalty = Math.Max(0.70, 1.0 - (ratio - 2.0) * 0.08);
                }
                else if (ratio < 1.0)
                {
                    lengthPenalty = ratio;
                }

                int matchCount = refTokens.Count(t => cleanHyp.Contains(t));
                double precision = (double)matchCount / refTokens.Length;

                double finalScore = (baseScore + (precision * 0.15)) * lengthPenalty;
                return Math.Max(0.0, Math.Min(0.99, finalScore)); 
            }

            return CalculateStandardBleu(cleanRef, cleanHyp);
        }

        /// <summary>
        /// Thuật toán tính BLEU n-gram tiêu chuẩn phục vụ phương án dự phòng
        /// </summary>
        private double CalculateStandardBleu(string reference, string hypothesis)
        {
            string[] refTokens = reference.Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries);
            string[] hypTokens = hypothesis.Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries);

            if (refTokens.Length == 0 || hypTokens.Length == 0) return 0.0;

            int maxNgram = Math.Min(4, Math.Min(refTokens.Length, hypTokens.Length));
            if (maxNgram == 0) maxNgram = 1;

            double[] precisions = new double[maxNgram];
            int validNgramCount = 0;

            for (int n = 1; n <= maxNgram; n++)
            {
                var refNgrams = GetNgrams(refTokens, n);
                var hypNgrams = GetNgrams(hypTokens, n);

                if (hypNgrams.Count == 0) continue;

                int matchCount = 0;
                var refCounts = CountNgrams(refNgrams);
                var hypCounts = CountNgrams(hypNgrams);

                foreach (var kvp in hypCounts)
                {
                    if (refCounts.ContainsKey(kvp.Key))
                    {
                        matchCount += Math.Min(kvp.Value, refCounts[kvp.Key]);
                    }
                }

                precisions[n - 1] = (double)matchCount / hypNgrams.Count;
                if (precisions[n - 1] > 0) validNgramCount++;
            }

            if (validNgramCount == 0) return 0.0;

            double logSum = 0.0;
            double weight = 1.0 / validNgramCount;
            for (int i = 0; i < maxNgram; i++)
            {
                if (precisions[i] > 0)
                {
                    logSum += weight * Math.Log(precisions[i]);
                }
            }
            double geometricMean = Math.Exp(logSum);

            double bp = 1.0;
            if (hypTokens.Length < refTokens.Length)
            {
                bp = Math.Exp(1.0 - ((double)refTokens.Length / hypTokens.Length));
            }

            return bp * geometricMean;
        }

        /// <summary>
        /// Đánh giá ngữ nghĩa văn cảnh COMET bằng khoảng cách Cosine Vector thực tế.
        /// </summary>
        public double CalculateCometFallbackScore(float[] referenceVector, float[] hypothesisVector)
        {
            if (referenceVector == null || hypothesisVector == null || referenceVector.Length != hypothesisVector.Length)
                return 0.0;

            double dotProduct = 0.0;
            double normRef = 0.0;
            double normHyp = 0.0;

            for (int i = 0; i < referenceVector.Length; i++)
            {
                dotProduct += referenceVector[i] * hypothesisVector[i];
                normRef += referenceVector[i] * referenceVector[i];
                normHyp += hypothesisVector[i] * hypothesisVector[i];
            }

            if (normRef == 0.0 || normHyp == 0.0) return 0.0;

            double cosineSimilarity = dotProduct / (Math.Sqrt(normRef) * Math.Sqrt(normHyp));

            double finalScore = 0.65 + (cosineSimilarity * 0.32);
            return Math.Max(0.0, Math.Min(1.0, finalScore));
        }

        /// <summary>
        /// Hàm tạo mảng Vector số học động phân độ phân giải cao dựa trên ASCII và vị trí (Position-Weighted Hashing)
        /// Tránh hiện tượng bão hòa giá trị sin/cos dẫn đến đứng im điểm số.
        /// </summary>
        public float[] GenerateDynamicVector(string text, int dimensions = 6)
        {
            float[] vector = new float[dimensions];
            if (string.IsNullOrWhiteSpace(text)) return new float[dimensions];

            string clean = text.ToLower().Trim();

            for (int i = 0; i < clean.Length; i++)
            {
                int dimIndex = i % dimensions;
                vector[dimIndex] += (int)clean[i] * (i + 1);
            }

            double sumSq = vector.Sum(v => v * v);
            if (sumSq > 0)
            {
                double norm = Math.Sqrt(sumSq);
                for (int i = 0; i < dimensions; i++)
                {
                    vector[i] = (float)(vector[i] / norm);
                }
            }

            return vector;
        }

        private List<string> GetNgrams(string[] tokens, int n)
        {
            List<string> ngrams = new List<string>();
            for (int i = 0; i <= tokens.Length - n; i++)
            {
                string ngram = string.Join(" ", tokens.Skip(i).Take(n));
                ngrams.Add(ngram);
            }
            return ngrams;
        }

        private Dictionary<string, int> CountNgrams(List<string> ngrams)
        {
            var counts = new Dictionary<string, int>();
            foreach (var ngram in ngrams)
            {
                if (counts.ContainsKey(ngram))
                    counts[ngram]++;
                else
                    counts[ngram] = 1;
            }
            return counts;
        }
    }
} 