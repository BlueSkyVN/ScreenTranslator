# Screen Translator

A Real-Time Screen Translation tool built with C# WPF, strictly following the **MVVM (Model-View-ViewModel)** architectural pattern. 

This application captures a selected region of your screen, performs Optical Character Recognition (OCR) using native Windows APIs, and translates the recognized text in real-time using Large Language Models (LLMs) via Groq and OpenAI APIs. The translated text is displayed on a transparent, draggable overlay window.

## Features
- **Real-Time OCR & Translation:** Automatically translates text from any application on your screen.
- **MVVM Architecture:** Clean, maintainable codebase separating UI (Views), Logic (ViewModels), and Business Rules (Services).
- **Native Windows OCR:** Fast and offline text recognition using `Windows.Media.Ocr`.
- **Multiple LLM Backends:** Supports lightning-fast translation via Groq (Llama 3) or OpenAI (GPT-4o-mini).
- **Custom Region Selection:** Draggable overlay to select any specific area on multi-monitor setups.
- **Draggable UI Overlay:** Borderless, transparent text display that floats above other apps.

## System Architecture (MVVM)
The project is decoupled into:
- **Views (`MainWindow`, `OverlayWindow`, `RegionSelectionWindow`)**: XAML files handling UI rendering. Code-behind files are kept empty, relying purely on Data Binding.
- **ViewModels (`MainViewModel`)**: The brain of the application. Handles translation loops, ICommands, and `INotifyPropertyChanged` data bindings.
- **Services (`ScreenCaptureService`, `OcrService`, `TranslationService`)**: Core business logic for interacting with OS APIs and external HTTP endpoints.

## Testing & Performance Benchmarks
The repository includes a dedicated xUnit test project (`ScreenTranslator.Tests`) to ensure reliability and prove real-time capabilities.

- **Scenario Tests:** Verifies safe exception handling (e.g., catching `401 Unauthorized` for invalid API keys without crashing).
- **Empty State Optimization:** Empty text recognition evaluates in `< 1ms` to save API bandwidth and CPU usage.
- **Real-time Benchmarking:** 
  - **Groq (Llama 3.1 8B):** Achieves an astonishing inference speed of **~360ms**. This sits well below the strict **< 1000ms threshold**, proving the application's true "Real-time" capabilities.
  - **OpenAI (GPT-4o-mini):** Offers higher accuracy and smoother context translation, at a slight trade-off in speed.

## Prerequisites
- Windows 10/11 (required for native Windows Media OCR).
- .NET 8.0 / 10.0 SDK.
- API Key from [Groq](https://console.groq.com/) or [OpenAI](https://platform.openai.com/).

## Getting Started
1. Clone the repository: `git clone https://github.com/BlueSkyVN/ScreenTranslator.git`
2. Open `ScreenTranslator.slnx` or the project folder in Visual Studio.
3. Build and run the project.
4. Input your API Key in the Main Window, select a screen region, and check "Start Real-Time Translation".
