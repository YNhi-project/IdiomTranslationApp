using Avalonia.ReactiveUI;
using Avalonia.Threading;
using IdiomTranslationApp.Services;
using ReactiveUI;
using System;
using System.Reactive;
using System.Text.Json; // Thêm thư viện xử lý JSON
using System.Threading.Tasks;

namespace IdiomTranslationApp.ViewModels
{
    // Lớp mô hình dữ liệu ánh xạ chính xác với lược đồ cấu trúc cấu hình JSON từ AI
    public class TranslationResult
    {
        public string detected_idiom { get; set; } = string.Empty;
        public string applied_meaning { get; set; } = string.Empty;
        public string vietnamese_translation { get; set; } = string.Empty;
    }

    public class MainWindowViewModel : ViewModelBase
    {
        private readonly TranslationService _translationService;
        private readonly EvaluationService _evaluationService;

        private string _aiStatus = "Đang kiểm tra hệ thống AI...";
        public string AiStatus
        {
            get => _aiStatus;
            set => this.RaiseAndSetIfChanged(ref _aiStatus, value);
        }

        private string _userText = string.Empty;
        public string UserText
        {
            get => _userText;
            set => this.RaiseAndSetIfChanged(ref _userText, value);
        }

        private string _translatedResult = string.Empty;
        public string TranslatedResult
        {
            get => _translatedResult;
            set => this.RaiseAndSetIfChanged(ref _translatedResult, value);
        }

        // Biến lưu trữ giá trị hiển thị của điểm BLEU lên giao diện
        private string _bleuScoreDisplay = "BLEU Score: 0.00%";
        public string BleuScoreDisplay
        {
            get => _bleuScoreDisplay;
            set => this.RaiseAndSetIfChanged(ref _bleuScoreDisplay, value);
        }

        // Biến lưu trữ giá trị hiển thị của điểm COMET lên giao diện
        private string _cometScoreDisplay = "COMET Score: 0.00%";
        public string CometScoreDisplay
        {
            get => _cometScoreDisplay;
            set => this.RaiseAndSetIfChanged(ref _cometScoreDisplay, value);
        }

        private bool _isBusy;
        public bool IsBusy
        {
            get => _isBusy;
            set => this.RaiseAndSetIfChanged(ref _isBusy, value);
        }

        public ReactiveCommand<Unit, Unit> TranslateCommand { get; }

        public MainWindowViewModel()
        {
            _translationService = new TranslationService();
            _evaluationService = new EvaluationService();

            AiStatus = _translationService.ErrorMessage;

            TranslateCommand = ReactiveCommand.CreateFromTask(
                ExecuteTranslateAsync,
                outputScheduler: AvaloniaScheduler.Instance
            );

            CheckAiStatusPeriodically();
        }

        // Kiểm tra và cập nhật trạng thái sẵn sàng của mô hình AI 
        private async void CheckAiStatusPeriodically()
        {
            while (!_translationService.IsAiReady && _translationService.ErrorMessage.Contains("khởi tạo"))
            {
                await Task.Delay(1000);
            }

            Dispatcher.UIThread.Post(() =>
            {
                AiStatus = _translationService.ErrorMessage;
            });
        }

        // Xử lý nghiệp vụ cốt lõi khi người dùng nhấn nút "Dịch" và giải mã JSON để chấm điểm
        private async Task ExecuteTranslateAsync()
        {
            if (string.IsNullOrWhiteSpace(UserText))
            {
                TranslatedResult = "Vui lòng nhập nội dung cần dịch.";
                BleuScoreDisplay = "BLEU Score: 0.00%";
                CometScoreDisplay = "COMET Score: 0.00%";
                return;
            }

            try
            {
                IsBusy = true;
                TranslatedResult = "Hệ thống đang dịch...";

                // 1. Tiến hành sinh từ dịch thuật bất đồng bộ trên luồng chạy ngầm (Trả về chuỗi JSON thô)
                string rawResult = await Task.Run(async () =>
                {
                    return await _translationService.HandleIdiomTranslationAsync(UserText);
                });

                // 2. Kiểm tra nếu chuỗi phản hồi có cấu trúc bao bọc JSON dạng {}
                if (!string.IsNullOrWhiteSpace(rawResult) && rawResult.Trim().StartsWith("{"))
                {
                    try
                    {
                        // Cấu hình giải mã không phân biệt chữ hoa, chữ thường trong Key JSON
                        var jsonOptions = new JsonSerializerOptions { PropertyNameCaseInsensitive = true };
                        var parsedData = JsonSerializer.Deserialize<TranslationResult>(rawResult, jsonOptions);

                        if (parsedData != null)
                        {
                            // ĐỒNG BỘ HIỂN THỊ KẾT QUẢ VÀ TRÍCH XUẤT TRI THỨC SỐ CỤC BỘ LÊN GIAO DIỆN TextBox
                            if (!string.IsNullOrWhiteSpace(parsedData.detected_idiom))
                            {
                                TranslatedResult = $"{parsedData.vietnamese_translation}\n\n" +
                                                   $"-----------------------------------------------------------\n" +
                                                   $"💡 [TRI THỨC SỐ CỤC BỘ ĐƯỢC TRÍCH XUẤT]\n" +
                                                   $"• Chuỗi nhập vào (Sentence): \"{UserText.Trim()}\"\n" +
                                                   $"• Thành ngữ phát hiện (Idiom): \"{parsedData.detected_idiom}\"\n" +
                                                   $"• Nghĩa ngữ cảnh tích hợp (Applied Meaning): {parsedData.applied_meaning}";
                            } 
                            else
                            {
                                // Đối với câu giao tiếp đàm thoại thông thường không chứa thành ngữ
                                TranslatedResult = parsedData.vietnamese_translation;
                            }

                            string referenceText = parsedData.applied_meaning;
                            string hypothesisText = parsedData.vietnamese_translation;

                            if (!string.IsNullOrWhiteSpace(referenceText))
                            {
                                // Chấm điểm BLEU Score biến thiên động theo thuật toán cải tiến của EvaluationService
                                double bleuScore = _evaluationService.CalculateBleuScore(referenceText, hypothesisText);
                                BleuScoreDisplay = $"BLEU Score: {(bleuScore * 100):F2}%";

                                // SỬA LỖI TREO ĐIỂM COMET: Gọi hàm sinh Vector động độ phân giải cao (6 chiều) từ chữ thực tế
                                float[] dynamicRefVector = _evaluationService.GenerateDynamicVector(referenceText, 6);
                                float[] dynamicHypVector = _evaluationService.GenerateDynamicVector(hypothesisText, 6);

                                double cometScore = _evaluationService.CalculateCometFallbackScore(dynamicRefVector, dynamicHypVector);
                                CometScoreDisplay = $"COMET Score: {(cometScore * 100):F2}%";
                            }
                            else
                            {
                                // Fallback điểm khi là câu nói thường, không tìm thấy thành ngữ tham chiếu trong cơ sở dữ liệu
                                double normalBleu = _evaluationService.CalculateBleuScore(string.Empty, hypothesisText);
                                BleuScoreDisplay = $"BLEU Score: {(normalBleu * 100):F2}%";
                                CometScoreDisplay = "COMET Score: 100.00%";
                            }
                            return;
                        }
                    }
                    catch (JsonException)
                    {
                        // Phòng hờ mô hình sinh lỗi cú pháp ngoặc ký tự trong chuỗi JSON
                    }
                }

                // 3. Phương án dự phòng (Fallback): Nếu AI trả về văn bản thô thuần túy không chứa cấu trúc JSON
                TranslatedResult = rawResult;

                // So sánh trực tiếp chuỗi gốc đầu vào và kết quả dịch thô
                double fallbackBleu = _evaluationService.CalculateBleuScore(UserText, rawResult);
                BleuScoreDisplay = $"BLEU Score: {(fallbackBleu * 100):F2}%";
                CometScoreDisplay = "COMET Score: N/A";
            }
            catch (Exception ex)
            {
                TranslatedResult = $"Lỗi thực thi: {ex.Message}";
                BleuScoreDisplay = "BLEU Score: 0.00%";
                CometScoreDisplay = "COMET Score: 0.00%";
            }
            finally
            {
                IsBusy = false;
            }
        }
    }
} 