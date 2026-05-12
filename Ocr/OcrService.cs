using System;
using System.Linq;
using System.Threading.Tasks;
using Windows.Graphics.Imaging;
using Windows.Media.Ocr;

namespace ScreenTranslator.Ocr
{
    public class OcrService
    {
        private OcrEngine? _ocrEngine;

        public OcrService(string language = "en-US")
        {
            var lang = new Windows.Globalization.Language(language);
            if (OcrEngine.IsLanguageSupported(lang))
            {
                _ocrEngine = OcrEngine.TryCreateFromLanguage(lang);
            }
            else
            {
                // Fallback to default user profile language
                _ocrEngine = OcrEngine.TryCreateFromUserProfileLanguages();
            }
        }

        /// <summary>
        /// Extracts text from a SoftwareBitmap
        /// </summary>
        public async Task<string> RecognizeTextAsync(SoftwareBitmap bitmap)
        {
            if (_ocrEngine == null || bitmap == null)
                return string.Empty;

            try
            {
                var ocrResult = await _ocrEngine.RecognizeAsync(bitmap);
                
                // Combine all text lines
                var rawText = string.Join(" ", ocrResult.Lines.Select(line => line.Text));
                
                // Filter out garbage characters that often appear from poor OCR
                var cleanedText = System.Text.RegularExpressions.Regex.Replace(rawText, @"[\|\~\\\/\^_{}\[\]\n\r]+", " ");
                
                // Collapse multiple spaces
                cleanedText = System.Text.RegularExpressions.Regex.Replace(cleanedText, @"\s+", " ").Trim();
                
                return cleanedText;
            }
            catch (Exception ex)
            {
                Console.WriteLine($"OCR Error: {ex.Message}");
                return string.Empty;
            }
        }
    }
}
