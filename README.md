# Screen Translator

A Real-Time Screen Translation tool built with C# WPF, designed following the **MVVM (Model-View-ViewModel)** architectural pattern. 

This application captures a selected region of your screen, performs Optical Character Recognition (OCR) using native Windows APIs, and translates the recognized text in real-time using large language models (LLMs) via Groq and OpenAI APIs. The translated text is displayed on a transparent, draggable overlay window.

## Features
- **Real-Time OCR & Translation:** Automatically translates text from any application on your screen.
- **MVVM Architecture:** Clean, maintainable codebase separating UI (Views), Logic (ViewModels), and Business Rules (Services).
- **Native Windows OCR:** Fast and offline text recognition using `Windows.Media.Ocr`.
- **Multiple LLM Backends:** Supports lightning-fast translation via Groq (Llama 3) or OpenAI (GPT-4o-mini).
- **Custom Region Selection:** Draggable overlay to select any specific area on multi-monitor setups.
- **Draggable UI Overlay:** Borderless, transparent text display that floats above other apps.

## System Architecture (MVVM)

The project strictly follows the MVVM pattern:
- **Views (`MainWindow`, `OverlayWindow`, `RegionSelectionWindow`)**: XAML files handling UI rendering.
- **ViewModels (`MainViewModel`)**: Handles translation loops, commands, and data bindings.
- **Services (`ScreenCaptureService`, `OcrService`, `TranslationService`)**: Core logic for interacting with OS APIs and external HTTP endpoints.

## Prerequisites
- Windows 10/11 (required for native Windows Media OCR).
- .NET 8.0 / .NET 10.0 SDK.
- API Key from [Groq](https://console.groq.com/) or [OpenAI](https://platform.openai.com/).

## Getting Started
1. Clone the repository.
2. Open `ScreenTranslator.sln` (or the folder) in Visual Studio.
3. Build and run the project.
4. Input your API Key in the Main Window, select a screen region, and check "Start Real-Time Translation".
