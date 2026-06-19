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
        
        // Fix #6: Biên dịch Regex 1 lần duy nhất thay vì tạo mới mỗi lần OCR
        private static readonly System.Text.RegularExpressions.Regex _garbageRegex = 
            new(@"[\|\~\\\/\^_{}\[\]\n\r]+", System.Text.RegularExpressions.RegexOptions.Compiled);
        private static readonly System.Text.RegularExpressions.Regex _whitespaceRegex = 
            new(@"\s+", System.Text.RegularExpressions.RegexOptions.Compiled);

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
                var cleanedText = _garbageRegex.Replace(rawText, " ");
                
                // Collapse multiple spaces
                cleanedText = _whitespaceRegex.Replace(cleanedText, " ").Trim();
                
                return cleanedText;
            }
            catch (Exception ex)
            {
                Infrastructure.LogService.Instance.Error("OcrService", $"OCR Error: {ex.Message}");
                return string.Empty;
            }
        }
    }
}
