# IDIAT System - Offline English-Vietnamese Idiom Translation

> Cross-platform Desktop Application for English-Vietnamese Idiom Translation leveraging Offline Local LLM (Qwen 4-bit Quantization) and C# .NET.

## 📌 Key Features
- **Regex Pattern Matching:** Dynamic idiom extraction & context lookup via Entity Framework Core.
- **Offline Inference Engine:** Runs quantized Qwen model via `LLama.NET` (GGUF format) without Cloud API fees or privacy leaks.
- **In-Context Learning:** Few-shot prompting & Semantic Anchoring with constrained JSON outputs (`temperature = 0.0`).
- **Evaluation Metrics:** Automated translation quality scoring using BLEU Score and COMET Metric.

## 🛠 Tech Stack
- **Language:** C# (.NET)
- **UI Framework:** Avalonia UI / WPF
- **Database & ORM:** SQL Server, Entity Framework Core
- **AI / LLM:** Qwen (GGUF 4-bit), LLama.NET, Prompt Engineering

## 🚀 How to Run Locally
1. Clone this repository:
   ```bash
   git clone [https://github.com/YNhi-project/IdiomTranslationApp.git](https://github.com/YNhi-project/IdiomTranslationApp.git)
