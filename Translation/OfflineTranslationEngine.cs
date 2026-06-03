using System;
using System.IO;
using System.Text;
using System.Text.Json;
using System.Collections.Generic;
using System.Threading.Tasks;
using Microsoft.ML.OnnxRuntime;
using Microsoft.ML.OnnxRuntime.Tensors;

namespace ScreenTranslator.Translation
{
    /// <summary>
    /// Engine dịch thuật ngoại tuyến sử dụng Microsoft ONNX Runtime và mô hình Helsinki-NLP (opus-mt-en-vi).
    /// Hỗ trợ chạy hoàn toàn Offline trên CPU/GPU của khách hàng mà không cần Internet hay API Key.
    /// </summary>
    public class OfflineTranslationEngine : IDisposable
    {
        private InferenceSession? _encoderSession;
        private InferenceSession? _decoderSession;
        private Dictionary<string, int>? _vocab;
        private Dictionary<int, string>? _invVocab;
        
        private readonly string _modelDir;
        private readonly string _encoderPath;
        private readonly string _decoderPath;
        private readonly string _vocabPath;
        
        public bool IsInitialized { get; private set; } = false;

        public OfflineTranslationEngine()
        {
            // Thiết lập thư mục chứa mô hình ngoại tuyến trong thư mục ứng dụng
            _modelDir = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "local_model");
            _encoderPath = Path.Combine(_modelDir, "encoder.onnx");
            _decoderPath = Path.Combine(_modelDir, "decoder.onnx");
            _vocabPath = Path.Combine(_modelDir, "vocab.json");
        }

        /// <summary>
        /// Kiểm tra xem người dùng đã tải mô hình Offline về máy chưa.
        /// </summary>
        public bool IsModelAvailable()
        {
            bool hasEncoder = File.Exists(_encoderPath) || File.Exists(Path.Combine(_modelDir, "encoder_model.onnx"));
            bool hasDecoder = File.Exists(_decoderPath) || File.Exists(Path.Combine(_modelDir, "decoder_model.onnx"));
            bool hasVocab = File.Exists(_vocabPath);
            return hasEncoder && hasDecoder && hasVocab;
        }

        /// <summary>
        /// Khởi tạo và nạp mô hình vào RAM/VRAM.
        /// </summary>
        public bool Initialize()
        {
            if (IsInitialized) return true;

            try
            {
                if (!IsModelAvailable())
                {
                    Console.WriteLine("Offline model files are not found. Offline mode is disabled.");
                    return false;
                }

                // Nạp từ vựng Vocab JSON
                string vocabJson = File.ReadAllText(_vocabPath);
                _vocab = JsonSerializer.Deserialize<Dictionary<string, int>>(vocabJson);
                
                if (_vocab != null)
                {
                    _invVocab = new Dictionary<int, string>();
                    foreach (var pair in _vocab)
                    {
                        _invVocab[pair.Value] = pair.Key;
                    }
                }

                // Khởi tạo các phiên suy luận ONNX (Inference Session) cho cả Encoder và Decoder
                var options = new SessionOptions();
                options.GraphOptimizationLevel = GraphOptimizationLevel.ORT_ENABLE_ALL; // Kích hoạt tối ưu hóa đồ thị ONNX toàn diện
                options.IntraOpNumThreads = Math.Max(2, Environment.ProcessorCount / 2); // Khai thác đa luồng CPU tối ưu
                options.ExecutionMode = ExecutionMode.ORT_SEQUENTIAL;
                options.AppendExecutionProvider_CPU(); // Chạy trên CPU cực kỳ ổn định và nhẹ nhàng

                string actualEncoderPath = File.Exists(_encoderPath) ? _encoderPath : Path.Combine(_modelDir, "encoder_model.onnx");
                string actualDecoderPath = File.Exists(_decoderPath) ? _decoderPath : Path.Combine(_modelDir, "decoder_model.onnx");

                _encoderSession = new InferenceSession(actualEncoderPath, options);
                _decoderSession = new InferenceSession(actualDecoderPath, options);

                IsInitialized = true;
                Console.WriteLine("Offline Translation Engine successfully initialized!");
                return true;
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Failed to initialize Offline Engine: {ex.Message}");
                return false;
            }
        }

        /// <summary>
        /// Thực hiện dịch văn bản từ Tiếng Anh sang Tiếng Việt Ngoại tuyến.
        /// </summary>
        public async Task<string> TranslateAsync(string text)
        {
            if (!IsInitialized)
            {
                if (!Initialize())
                {
                    return "[Lỗi Offline]: Không tìm thấy tệp mô hình ngoại tuyến. Vui lòng tải 'encoder.onnx', 'decoder.onnx' và 'vocab.json' đặt vào thư mục 'local_model/' của ứng dụng để dịch Offline.";
                }
            }

            if (string.IsNullOrWhiteSpace(text) || _vocab == null || _encoderSession == null || _decoderSession == null)
                return string.Empty;

            try
            {
                return await Task.Run(() =>
                {
                    // 1. Mã hóa văn bản đầu vào thành Token IDs (Tokenization)
                    var inputTokens = Tokenize(text);
                    if (inputTokens.Count == 0) return string.Empty;

                    // Thêm token kết thúc câu <eos> (thường là ID = 2 hoặc tùy theo vocab)
                    inputTokens.Add(GetTokenId("</s>", 2)); 

                    int batchSize = 1;
                    int sequenceLength = inputTokens.Count;

                    // 2. Chuẩn bị tensor đầu vào cho Encoder
                    long[] inputIdsArray = new long[sequenceLength];
                    long[] attentionMaskArray = new long[sequenceLength];
                    for (int i = 0; i < sequenceLength; i++)
                    {
                        inputIdsArray[i] = inputTokens[i];
                        attentionMaskArray[i] = 1; // 1 nghĩa là chú ý đến token này
                    }

                    var inputIdsTensor = new DenseTensor<long>(inputIdsArray, new int[] { batchSize, sequenceLength });
                    var attentionMaskTensor = new DenseTensor<long>(attentionMaskArray, new int[] { batchSize, sequenceLength });

                    var encoderInputs = new List<NamedOnnxValue>
                    {
                        NamedOnnxValue.CreateFromTensor("input_ids", inputIdsTensor),
                        NamedOnnxValue.CreateFromTensor("attention_mask", attentionMaskTensor)
                    };

                    // 3. Chạy Encoder ONNX để trích xuất ma trận đặc trưng ngữ cảnh (encoder_hidden_states)
                    using var encoderResults = _encoderSession.Run(encoderInputs);
                    var encoderHiddenStates = encoderResults[0].AsTensor<float>();

                    // 4. Bắt đầu vòng lặp Decoder để sinh chữ từng từ một (Greedy Decoding Loop)
                    var decodedTokens = new List<int>();
                    int maxGenerateLength = 50; // Giới hạn độ dài tối đa cho phụ đề/OCR
                    
                    // Token bắt đầu giải mã (Helsinki-NLP sử dụng token rỗng hoặc </s> đầu tiên)
                    int currentDecoderToken = GetTokenId("</s>", 2); 
                    decodedTokens.Add(currentDecoderToken);

                    int consecutiveDuplicateCount = 0;
                    int lastTokenId = -1;

                    for (int step = 0; step < maxGenerateLength; step++)
                    {
                        // Chuẩn bị đầu vào cho Decoder
                        long[] decoderInputArray = new long[decodedTokens.Count];
                        for (int i = 0; i < decodedTokens.Count; i++)
                        {
                            decoderInputArray[i] = decodedTokens[i];
                        }
                        
                        var decoderInputTensor = new DenseTensor<long>(decoderInputArray, new int[] { batchSize, decodedTokens.Count });

                        var decoderInputs = new List<NamedOnnxValue>
                        {
                            NamedOnnxValue.CreateFromTensor("input_ids", decoderInputTensor),
                            NamedOnnxValue.CreateFromTensor("encoder_hidden_states", encoderHiddenStates),
                            NamedOnnxValue.CreateFromTensor("encoder_attention_mask", attentionMaskTensor)
                        };

                        // Chạy Decoder
                        using var decoderResults = _decoderSession.Run(decoderInputs);
                        var logitsTensor = decoderResults[0].AsTensor<float>();

                        // Lấy xác suất của token cuối cùng trong chuỗi logits
                        int vocabSize = (int)logitsTensor.Dimensions[2];
                        int lastTokenIndex = decodedTokens.Count - 1;

                        // Áp dụng Repetition Penalty (Phạt lặp từ để tránh lặp cụm vô nghĩa)
                        float repetitionPenalty = 1.15f; 
                        var logits = new float[vocabSize];
                        for (int v = 0; v < vocabSize; v++)
                        {
                            float logit = logitsTensor[0, lastTokenIndex, v];
                            if (decodedTokens.Contains(v))
                            {
                                logit = logit > 0 ? logit / repetitionPenalty : logit * repetitionPenalty;
                            }
                            logits[v] = logit;
                        }

                        int nextTokenId = 2; // Mặc định là </s>
                        float maxLogit = float.MinValue;

                        // Tìm Token ID có trọng số xác suất cao nhất (Greedy Search)
                        for (int v = 0; v < vocabSize; v++)
                        {
                            if (logits[v] > maxLogit)
                            {
                                maxLogit = logits[v];
                                nextTokenId = v;
                            }
                        }

                        // Nếu gặp token kết thúc câu hoặc token rác thì dừng
                        if (nextTokenId == GetTokenId("</s>", 2) || nextTokenId == GetTokenId("</S>", 2) || nextTokenId == GetTokenId("<pad>", 65000))
                        {
                            break;
                        }

                        // Phát hiện lặp cụm từ vô hạn để dừng sớm
                        if (nextTokenId == lastTokenId)
                        {
                            consecutiveDuplicateCount++;
                            if (consecutiveDuplicateCount >= 3)
                            {
                                break;
                            }
                        }
                        else
                        {
                            consecutiveDuplicateCount = 0;
                        }
                        lastTokenId = nextTokenId;

                        decodedTokens.Add(nextTokenId);
                    }

                    // 5. Giải mã dãy Token IDs thành chuỗi tiếng Việt (Detokenization)
                    return Detokenize(decodedTokens);
                });
            }
            catch (Exception ex)
            {
                return $"[Lỗi suy luận Offline]: {ex.Message}";
            }
        }

        private List<int> Tokenize(string text)
        {
            var tokens = new List<int>();
            if (string.IsNullOrWhiteSpace(text) || _vocab == null) return tokens;

            // 1. Chuẩn hóa chuỗi, loại bỏ khoảng trắng thừa và thay thế bằng ký tự SentencePiece U+2581 (  )
            string cleaned = System.Text.RegularExpressions.Regex.Replace(text, @"\s+", " ").Trim();
            string normalized = cleaned.Replace(" ", "\u2581");
            if (!normalized.StartsWith("\u2581"))
            {
                normalized = "\u2581" + normalized;
            }

            // 2. Thuật toán Greedy MaxMatch (Phân tách từ tối đại thành các Subwords)
            int i = 0;
            while (i < normalized.Length)
            {
                int longestMatchLen = 0;
                int longestMatchId = -1;

                // Thử các độ dài prefix từ dài nhất đến ngắn nhất để tìm BPE Subword khớp nhất
                int maxLen = Math.Min(normalized.Length - i, 30);
                for (int len = maxLen; len >= 1; len--)
                {
                    string sub = normalized.Substring(i, len);
                    if (_vocab.TryGetValue(sub, out int id))
                    {
                        longestMatchLen = len;
                        longestMatchId = id;
                        break;
                    }
                }

                if (longestMatchLen > 0)
                {
                    tokens.Add(longestMatchId);
                    i += longestMatchLen;
                }
                else
                {
                    // Nếu gặp ký tự không nằm trong từ điển, gán token <unk> (ID = 1) và tiến lên 1 ký tự
                    tokens.Add(GetTokenId("<unk>", 1));
                    i += 1;
                }
            }

            return tokens;
        }

        private string Detokenize(List<int> tokenIds)
        {
            if (tokenIds == null || tokenIds.Count == 0 || _invVocab == null)
                return string.Empty;

            StringBuilder sb = new StringBuilder();
            foreach (int id in tokenIds)
            {
                // Bỏ qua các token điều khiển như </s> (EOS/PAD) hoặc <s>
                if (id == GetTokenId("</s>", 2) || id == GetTokenId("<s>", 0) || id == GetTokenId("<pad>", 65000))
                    continue;

                if (_invVocab.TryGetValue(id, out string? word))
                {
                    // Loại bỏ các thẻ điều khiển dạng text
                    if (word == "</s>" || word == "<s>" || word == "<pad>" || word == "<unk>")
                        continue;

                    sb.Append(word);
                }
            }

            // Thay thế ký tự đặc biệt U+2581 về lại khoảng trắng thực tế và làm sạch đầu ra
            string result = sb.ToString().Replace("\u2581", " ").Trim();
            
            // Viết hoa chữ cái đầu tiên cho chuẩn văn phong phụ đề
            if (result.Length > 0)
            {
                result = char.ToUpper(result[0]) + result.Substring(1);
            }
            return result;
        }

        private int GetTokenId(string key, int defaultValue)
        {
            if (_vocab != null && _vocab.TryGetValue(key, out int id))
            {
                return id;
            }
            return defaultValue;
        }

        public void Dispose()
        {
            _encoderSession?.Dispose();
            _decoderSession?.Dispose();
            IsInitialized = false;
        }
    }
}
