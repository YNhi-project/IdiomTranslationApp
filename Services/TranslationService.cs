using IdiomTranslationApp.Models;
using Microsoft.EntityFrameworkCore;
using LLama.Common;
using LLama;
using System;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading.Tasks;

namespace IdiomTranslationApp.Services
{
    public class TranslationService : IDisposable
    {
        private readonly string _modelPath;
        private LLamaWeights? _model;
        private LLamaContext? _context;
        private StatelessExecutor? _executor;

        public string ErrorMessage { get; private set; } = "[Hệ thống IDIAT đang khởi tạo...]";
        public bool IsAiReady { get; private set; } = false;

        public TranslationService()
        {
            string baseDir = AppDomain.CurrentDomain.BaseDirectory;
            _modelPath = Path.Combine(baseDir, "MLModel", "qwen2.5-1.5b-instruct-q4_k_m.gguf");

            try
            {
                InitializeLLama();
            }
            catch (Exception ex)
            {
                ErrorMessage = $"Lỗi nạp AI: {ex.Message}";
                IsAiReady = false;
            }
        }

        private void InitializeLLama()
        {
            if (!File.Exists(_modelPath))
            {
                ErrorMessage = "Không tìm thấy file mô hình Qwen trong thư mục MLModel!";
                return;
            }

            var parameters = new ModelParams(_modelPath)
            {
                ContextSize = 2048,
                GpuLayerCount = 0
            };

            _model = LLamaWeights.LoadFromFile(parameters);
            _context = _model.CreateContext(parameters);
            _executor = new StatelessExecutor(_model, parameters);

            IsAiReady = true;
            ErrorMessage = "Hệ thống AI đã sẵn sàng!";
        }

        public async Task<string> HandleIdiomTranslationAsync(string userInput)
        {
            try
            {
                using (var db = new IdiomTranslationDbContext())
                {
                    string S = userInput.Trim();

                    if (string.IsNullOrWhiteSpace(S))
                        return "{\"detected_idiom\":\"\",\"applied_meaning\":\"\",\"vietnamese_translation\":\"Vui lòng nhập văn bản cần dịch.\"}";

                    var idiomsList = await db.Idioms
                        .Include(i => i.FewShotExamples)
                        .AsNoTracking() 
                        .ToListAsync();

                    Idiom? detectedIdiom = null;

                    string cleanInput = Regex.Replace(S, @"\s+", " ");
                    cleanInput = cleanInput.Replace("’", "'").Replace("`", "'");

                    foreach (var idiom in idiomsList.OrderByDescending(i => i.Phrase?.Length ?? 0))
                    {
                        if (string.IsNullOrEmpty(idiom.Phrase)) continue;

                        string dbPhrase = idiom.Phrase.Trim().Replace("’", "'").Replace("`", "'");
                        dbPhrase = Regex.Replace(dbPhrase, @"\s+", " ");

                        string[] words = dbPhrase.Split(' ');
                        for (int i = 0; i < words.Length; i++)
                        {
                            string w = words[i];

                            if (string.Equals(w, "one's", StringComparison.OrdinalIgnoreCase) ||
                                string.Equals(w, "someone's", StringComparison.OrdinalIgnoreCase) ||
                                string.Equals(w, "sb's", StringComparison.OrdinalIgnoreCase))
                            {
                                words[i] = @"(?:[a-zA-Z0-9\s]+'s|my|your|his|her|their|our|its)";
                            }
                            else if (string.Equals(w, "sb", StringComparison.OrdinalIgnoreCase) ||
                                     string.Equals(w, "someone", StringComparison.OrdinalIgnoreCase))
                            {
                                words[i] = @"(?:[a-zA-Z0-9\s]+)";
                            }
                            else
                            {
                                words[i] = Regex.Escape(w); 
                            }
                        }

                        string pattern = string.Join(@"\s+", words);
                        pattern = @"(?<=\s|^)" + pattern + @"(?=\s|$|[\.,!\?])";

                        if (Regex.IsMatch(cleanInput, pattern, RegexOptions.IgnoreCase))
                        {
                            detectedIdiom = idiom; 
                            break;
                        }
                    }

                    string aiResult = "";
                    if (IsAiReady && _executor != null)
                    {
                        aiResult = await CallLocalLLamaModelAsync(S, detectedIdiom);
                    }
                    else
                    {
                        aiResult = "{\"detected_idiom\":\"\",\"applied_meaning\":\"\",\"vietnamese_translation\":\"[AI chưa sẵn sàng - Hệ thống đang ở chế độ từ điển tĩnh]\"}";
                    }

                    return aiResult;
                }
            }
            catch (Exception ex)
            {
                return $"{{\"detected_idiom\":\"\",\"applied_meaning\":\"\",\"vietnamese_translation\":\"Lỗi hệ thống: {ex.Message}\"}}";
            }
        }

        private async Task<string> CallLocalLLamaModelAsync(string S, Idiom? dbIdiom)
        {
            try
            {
                string prompt = "";
                var parameters = new ModelParams(_modelPath) { ContextSize = 2048, GpuLayerCount = 0 };
                var cleanExecutor = new StatelessExecutor(_model!, parameters);

                const string prefillSeparator = "<|im_start|>assistant\n";
                string cleanMeaning = "";

                if (dbIdiom != null)
                {
                    string D = dbIdiom.Meaning ?? "";
                    cleanMeaning = D.Split(',').FirstOrDefault()?.Trim() ?? D;
                    cleanMeaning = Regex.Replace(cleanMeaning, @"\b(của\s+)?ai\s+đó\b", "", RegexOptions.IgnoreCase).Trim();

                    bool isPureIdiom = string.Equals(S.Trim(), dbIdiom.Phrase?.Trim(), StringComparison.OrdinalIgnoreCase);

                    if (isPureIdiom)
                    {
                        prompt = "<|im_start|>system\n" +
                                 "You are IDIAT, an advanced linguistic dictionary engine.\n" +
                                 "Task: Output a valid JSON object containing the standalone idiom information.\n" +
                                 "CRITICAL CONSTRAINT: You MUST follow the exact JSON format below. Do not include markdown blocks like ```json or any conversational text.\n" +
                                 "{\n" +
                                 "  \"detected_idiom\": \"string\",\n" +
                                 "  \"applied_meaning\": \"string\",\n" +
                                 "  \"vietnamese_translation\": \"string\"\n" +
                                 "}<|im_end|>\n" +
                                 "<|im_start|>user\n" +
                                 $"Standalone Idiom: {S}\n" +
                                 $"Dictionary Definition: {cleanMeaning}\n" +
                                 "Provide the JSON response:<|im_end|>\n" +
                                 prefillSeparator +
                                 "{\n" +
                                 $"  \"detected_idiom\": \"{S}\",\n" +
                                 $"  \"applied_meaning\": \"{cleanMeaning}\",\n" +
                                 $"  \"vietnamese_translation\": \"{cleanMeaning}\"";
                    }
                    else
                    {
                        prompt = "<|im_start|>system\n" +
                                 "You are IDIAT, an expert English-to-Vietnamese translation engine specializing in database-driven idiom localization.\n" +
                                 "Task: Translate the full English sentence into fluent Vietnamese while integrating the provided mandatory phrase.\n" +
                                 "You MUST output the result as a strict single JSON object matching the exact schema below. Do not output markdown code blocks or any extra texts.\n" +
                                 "{\n" +
                                 "  \"detected_idiom\": \"string\",\n" +
                                 "  \"applied_meaning\": \"string\",\n" +
                                 "  \"vietnamese_translation\": \"string\"\n" +
                                 "}\n\n" +
                                 "--- SYSTEM-WIDE FEW-SHOT EXAMPLES ---\n" +
                                 "User: [Sentence]: \"Nhi is the apple of my eye\" | [Idiom]: \"the apple of one's eye\" | [MANDATORY PHRASE]: \"người quan trọng nhất\"\n" +
                                 "Assistant: {\"detected_idiom\":\"the apple of one's eye\",\"applied_meaning\":\"người quan trọng nhất\",\"vietnamese_translation\":\"Nhi là người quan trọng nhất của tôi.\"}\n\n" +
                                 "--- END OF EXAMPLES ---<|im_end|>\n" +
                                 "<|im_start|>user\n" +
                                 $"[Sentence]: \"{S}\"\n" +
                                 $"[Idiom]: \"{dbIdiom.Phrase}\"\n" +
                                 $"[MANDATORY PHRASE]: \"{cleanMeaning}\"\n" +
                                 "Translate now and output strict JSON:<|im_end|>\n" +
                                 prefillSeparator +
                                 "{\n" +
                                 $"  \"detected_idiom\": \"{dbIdiom.Phrase}\",\n" +
                                 $"  \"applied_meaning\": \"{cleanMeaning}\",\n" +
                                 "  \"vietnamese_translation\": \"";
                    }
                }
                else
                {
                    prompt = "<|im_start|>system\n" +
                             "You are a professional Neural Machine Translation model.\n" +
                             "Task: Translate the user input sentence directly into natural Vietnamese.\n" +
                             "You MUST output the result as a strict single JSON object matching the exact schema below. Do not output markdown or any conversational note.\n" +
                             "{\n" +
                             "  \"detected_idiom\": \"\",\n" +
                             "  \"applied_meaning\": \"\",\n" +
                             "  \"vietnamese_translation\": \"string\"\n" +
                             "}<|im_end|>\n" +
                             "<|im_start|>user\n" +
                             $"Sentence: {S}<|im_end|>\n" +
                             prefillSeparator +
                             "{\n" +
                             "  \"detected_idiom\": \"\",\n" +
                             "  \"applied_meaning\": \"\",\n" +
                             "  \"vietnamese_translation\": \"";
                }

                string responseText = "";

                var samplingPipeline = new LLama.Sampling.DefaultSamplingPipeline() { Temperature = 0.0f, TopK = 1, TopP = 0.1f };
                var inferenceParams = new InferenceParams()
                {
                    MaxTokens = 128,
                    AntiPrompts = new[] { "<|im_end|>", "<|im_start|>", "assistant\n", "user\n" },
                    SamplingPipeline = samplingPipeline
                };

                await foreach (var token in cleanExecutor.InferAsync(prompt, inferenceParams))
                {
                    responseText += token;
                }

                string finalResult = "";
                if (dbIdiom != null && string.Equals(S.Trim(), dbIdiom.Phrase?.Trim(), StringComparison.OrdinalIgnoreCase))
                {
                    finalResult = "{\n" +
                                  $"  \"detected_idiom\": \"{S}\",\n" +
                                  $"  \"applied_meaning\": \"{cleanMeaning}\",\n" +
                                  $"  \"vietnamese_translation\": \"{cleanMeaning}\"" + responseText;
                }
                else if (dbIdiom != null)
                {
                    finalResult = "{\n" +
                                  $"  \"detected_idiom\": \"{dbIdiom.Phrase}\",\n" +
                                  $"  \"applied_meaning\": \"{cleanMeaning}\",\n" +
                                  "  \"vietnamese_translation\": \"" + responseText;
                }
                else
                {
                    finalResult = "{\n" +
                                  "  \"detected_idiom\": \"\",\n" +
                                  "  \"applied_meaning\": \"\",\n" +
                                  "  \"vietnamese_translation\": \"" + responseText;
                }

                finalResult = finalResult.Trim();
                if (!finalResult.EndsWith("}"))
                {
                    if (finalResult.EndsWith("\""))
                        finalResult += "}";
                    else
                        finalResult += "\"}";
                }

                string[] junkLabels = { "<|im_end|>", "<|im_start|>", "<|assistant|>", "<|system|>", "<|user|>" };
                foreach (var junk in junkLabels)
                {
                    int index = finalResult.IndexOf(junk, StringComparison.OrdinalIgnoreCase);
                    if (index >= 0)
                    {
                        finalResult = finalResult.Substring(0, index).Trim();
                    }
                }

                if (!finalResult.EndsWith("}"))
                {
                    if (finalResult.EndsWith("\"")) finalResult += "}";
                    else finalResult += "\"}";
                }

                return finalResult;
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[AI INFERENCE ERROR]: {ex.Message}");
                return $"{{\"detected_idiom\":\"\",\"applied_meaning\":\"\",\"vietnamese_translation\":\"Hệ thống đang bận xử lý, vui lòng thử lại.\"}}";
            }
        }

        public void Dispose()
        {
            _context?.Dispose();
            _model?.Dispose();
        }
    }
} 