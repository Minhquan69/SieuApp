using System;
using System.Collections.Generic;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using System.IO;
using V3SClient.Services;

namespace VehicleDocumentProcessing.WPF.Services
{
    public class LLMConfig
    {
        public const string DefaultModel = "google/gemma-4-26B-A4B-it";
        public string ApiUrl { get; set; } = "";
        public string ApiKey { get; set; } = "";
        public string Model { get; set; } = DefaultModel;
        public int TimeoutSeconds { get; set; } = 180;
        public double Temperature { get; set; } = 0;
    }

    /// <summary>
    /// Kết quả gộp: vừa phân loại vừa trích xuất biển số trong 1 lần gọi LLM.
    /// </summary>
    public class UnifiedResult
    {
        [JsonProperty("loai_giay_to")]
        public string LoaiGiayTo { get; set; }

        [JsonProperty("bien_so")]
        public string BienSo { get; set; }

        [JsonProperty("mau_bien_so")]
        public string MauBienSo { get; set; }

        [JsonProperty("loai_xe")]
        public string LoaiXe { get; set; }

        [JsonProperty("mau_xe")]
        public string MauXe { get; set; }

        [JsonProperty("goc_chup")]
        public string GocChup { get; set; }

        [JsonProperty("ten_don")]
        public string TenDon { get; set; }

        [JsonExtensionData]
        public IDictionary<string, JToken> AdditionalData { get; set; }
    }

    [Obsolete("Dùng UnifiedResult thay thế. ClassificationResult không chứa trường biển số.")]
    public class ClassificationResult
    {
        [JsonProperty("loai_giay_to")]
        public string LoaiTaiLieu { get; set; }

        [JsonProperty("ten_don")]
        public string TenDon { get; set; }
    }

    public class ExtractedData
    {
        [JsonProperty("loai_giay_to")]
        public string LoaiGiayTo { get; set; }
        
        [JsonProperty("bien_so")]
        public string BienSo { get; set; }
        
        [JsonProperty("so_giay_dang_ky")]
        public string SoGiayDangKy { get; set; }

        [JsonProperty("LicensePlate")]
        public string LicensePlate { get; set; }
        
        // This dictionary will hold all other dynamically extracted fields
        [JsonExtensionData]
        public IDictionary<string, JToken> AdditionalData { get; set; }
    }

    public class BBox
    {
        [JsonProperty("x1")]
        public double X1 { get; set; }
        [JsonProperty("y1")]
        public double Y1 { get; set; }
        [JsonProperty("x2")]
        public double X2 { get; set; }
        [JsonProperty("y2")]
        public double Y2 { get; set; }
    }

    public class LLMClient
    {
        private readonly LLMConfig _config;
        private readonly ILogger<LLMClient> _logger;
        private readonly HttpClient _httpClient;
        private static readonly object PromptFileLock = new object();

        private static readonly HashSet<string> VALID_TYPES = new HashSet<string>
        {
            "Căn cước công dân",
            "Giấy phép lái xe",
            "Giấy đăng ký ô tô",
            "Giấy đăng ký mô tô/xe máy",
            "Đơn A4",
            "Ảnh xe ô tô",
            "Không xác định"
        };

        // ── Prompt gộp: Phân loại + Trích xuất biển số trong 1 lần gọi ──
        private const string UNIFIED_PROMPT = @"
Phân loại tài liệu hoặc ảnh Việt Nam trong ảnh, đồng thời trích xuất các thông tin.

Chỉ chọn đúng một giá trị cho ""loai_giay_to"":
- ""Căn cước công dân""
- ""Giấy phép lái xe""
- ""Giấy đăng ký ô tô""
- ""Giấy đăng ký mô tô/xe máy""
- ""Đơn A4""
- ""Ảnh xe ô tô""
- ""Không xác định""

Quy tắc phân loại:
- ""Căn cước công dân"", ""Giấy phép lái xe"", ""Giấy đăng ký ô tô"", ""Giấy đăng ký mô tô/xe máy"" là thẻ nhựa, thẻ giấy hoặc bản điện tử.
- ""Đơn A4"" là văn bản/biểu mẫu, không phải thẻ nhựa, giấy tờ xe.
- ""Ảnh xe ô tô"" là ảnh chụp xe ô tô thực tế (thường nhìn thấy biển số), KHÔNG phải giấy đăng ký xe.

Quy tắc trích xuất cho ""Ảnh xe ô tô"":
- ""bien_so"": Đọc chính xác ký tự trên biển số xe.
- ""loai_xe"": Xác định loại xe. Chỉ chọn: ""xe con"", ""xe tải"", ""xe buýt"", hoặc ""không rõ"".
- ""mau_xe"": Màu thân xe chính (chỉ chọn: trắng, đen, xám, bạc, đỏ, xanh dương, xanh lá, vàng, nâu, cam, hoặc không rõ).
- ""mau_bien_so"": Phân tích màu nền biển số. Chỉ chọn: ""trắng"", ""vàng"", ""xanh nước biển"", ""đỏ"", hoặc ""không rõ"". 
  + Biển ""vàng"" phải là vàng đậm/rõ trên phần lớn diện tích. Nếu nền sáng, xám nhạt, kem, bị bóng, hoặc chỉ hơi ngả vàng do ánh sáng thì trả về ""trắng"".
  + Trả về ""xanh nước biển"" hoặc ""đỏ"" khi màu nền hiển thị rõ ràng.
- ""goc_chup"": Xác định góc chụp xe. Chỉ chọn 1 trong 3 giá trị sau:
  + ""truoc"": Nếu chụp phía trước xe và thấy rõ đầu xe/thân xe.
  + ""sau"": Nếu chụp phía sau xe và thấy rõ đuôi xe/thân xe.
  + ""bsx"": Nếu ảnh chụp CẬN CẢNH (CLOSE-UP) phần lớn chỉ chứa biển số, KHÔNG THẤY RÕ đầu xe hay đuôi xe.
  Nếu thấy rõ đầu xe hoặc đuôi xe thì TUYỆT ĐỐI KHÔNG ĐƯỢC trả về ""bsx"". Nếu không chắc chắn, trả về chuỗi rỗng. Không suy diễn.

Quy tắc cho các loại khác:
- ""Giấy đăng ký ô tô/mô tô"": đọc trường ""Biển số:"" (KHÔNG nhầm với ""Số:"").
- ""Đơn A4"": đọc ""bien_so"" trong nội dung (nếu có). Đọc tên đơn/tiêu đề chính vào ""ten_don"".
- Nếu không phải ""Ảnh xe ô tô"": ""loai_xe"", ""mau_xe"", ""mau_bien_so"", ""goc_chup"" là chuỗi rỗng.
- Nếu không đọc được biển số: ""bien_so"" là chuỗi rỗng.
- Không phải ""Đơn A4"": ""ten_don"" là chuỗi rỗng.

Chỉ trả về JSON thuần hợp lệ (không markdown, không giải thích):

{
  ""loai_giay_to"": ""một giá trị trong danh sách"",
  ""bien_so"": ""biển số xe nếu có, ngược lại chuỗi rỗng"",
  ""mau_bien_so"": ""trắng, vàng, xanh nước biển, đỏ, không rõ (ngược lại chuỗi rỗng)"",
  ""loai_xe"": ""xe con, xe tải, xe buýt, không rõ (ngược lại chuỗi rỗng)"",
  ""mau_xe"": ""màu xe theo danh sách (ngược lại chuỗi rỗng)"",
  ""goc_chup"": ""truoc, sau, hoặc bsx (ngược lại chuỗi rỗng)"",
  ""ten_don"": ""tên đơn nếu là Đơn A4, ngược lại chuỗi rỗng""
}";

        // ── Giữ lại prompt cũ cho backward compatibility ──
        private const string CLASSIFICATION_PROMPT = @"
Phân loại tài liệu hoặc ảnh Việt Nam trong ảnh.

Chỉ chọn đúng một giá trị cho ""loai_giay_to"":
- ""Căn cước công dân""
- ""Giấy phép lái xe""
- ""Giấy đăng ký ô tô""
- ""Giấy đăng ký mô tô/xe máy""
- ""Đơn A4""
- ""Ảnh xe ô tô""
- ""Không xác định""

Quy tắc:
- ""Căn cước công dân"", ""Giấy phép lái xe"", ""Giấy đăng ký ô tô"", ""Giấy đăng ký mô tô/xe máy"" là các loại thẻ nhựa, thẻ giấy hoặc bản điện tử được mở trên màn hình điện thoại.
- ""Đơn A4"" là văn bản/đơn khổ giấy A4, thường có tiêu đề lớn ở phần đầu như ""ĐƠN ..."", ""GIẤY ..."", ""TỜ KHAI ..."" hoặc tên biểu mẫu hành chính.
- Chỉ gọi là ""Đơn A4"" khi đây là một trang văn bản/biểu mẫu A4, không phải thẻ nhựa, giấy tờ xe hoặc căn cước.
- ""Ảnh xe ô tô"" là ảnh chụp xe ô tô thực tế (có thể nhìn thấy biển số xe), KHÔNG phải giấy tờ đăng ký xe. Ảnh có thể là mặt trước, mặt sau, hoặc bên hông xe.
- Với ""Đơn A4"", đọc chính xác tên đơn/tiêu đề chính ở đầu văn bản và trả vào trường ""ten_don"". Không lấy khẩu hiệu, tên cơ quan, số văn bản hoặc nội dung thân đơn.
- Với các loại khác, ""ten_don"" phải là chuỗi rỗng.
- Không OCR thông tin cá nhân và không giải thích.
- Chỉ trả JSON hợp lệ:

{
  ""loai_giay_to"": ""một giá trị trong danh sách trên"",
  ""ten_don"": ""tên đơn nếu là Đơn A4, ngược lại là chuỗi rỗng""
}";

        private static readonly Dictionary<string, string> OCR_PROMPTS = new Dictionary<string, string>
        {
            { "Căn cước công dân", @"Ảnh là Căn cước công dân Việt Nam bản giấy hoặc bản điện tử.
Trích xuất các trường đọc được trên thẻ:
- Số định danh cá nhân
- Họ và tên
- Ngày sinh
- Giới tính
- Quốc tịch
- Quê quán
- Nơi thường trú
- Ngày cấp
- Ngày hết hạn
- Đặc điểm nhận dạng nếu có

Chỉ trả JSON hợp lệ. Giữ nguyên chữ đúng như trên thẻ.
Không tự suy đoán, không sửa lỗi, không thêm thông tin không đọc được.
Trường không đọc được phải có giá trị chuỗi rỗng.

{
  ""loai_giay_to"": ""Căn cước công dân"",
  ""so_dinh_danh_ca_nhan"": """",
  ""ho_va_ten"": """",
  ""ngay_sinh"": """",
  ""gioi_tinh"": """",
  ""quoc_tich"": """",
  ""que_quan"": """",
  ""noi_thuong_tru"": """",
  ""ngay_cap"": """",
  ""ngay_het_han"": """",
  ""dac_diem_nhan_dang"": """"
}" },
            { "Giấy phép lái xe", @"Ảnh là Giấy phép lái xe Việt Nam bản giấy hoặc bản điện tử.
Trích xuất các trường đọc được:
- Số giấy phép lái xe
- Họ và tên
- Ngày sinh
- Quốc tịch
- Nơi cư trú
- Hạng giấy phép
- Ngày cấp
- Ngày hết hạn
- Nơi cấp

Chỉ trả JSON hợp lệ. Giữ nguyên chữ đúng như in trên giấy phép.
Không tự suy đoán, không sửa lỗi, không thêm thông tin không đọc được.
Trường không đọc được phải có giá trị chuỗi rỗng.

{
  ""loai_giay_to"": ""Giấy phép lái xe"",
  ""so_giay_phep_lai_xe"": """",
  ""ho_va_ten"": """",
  ""ngay_sinh"": """",
  ""quoc_tich"": """",
  ""noi_cu_tru"": """",
  ""hang_giay_phep"": """",
  ""ngay_cap"": """",
  ""ngay_het_han"": """",
  ""noi_cap"": """"
}" },
            { "Giấy đăng ký ô tô", @"Ảnh là Giấy đăng ký ô tô Việt Nam hoặc bản điện tử dược chụp qua điện thoại.
Trích xuất các trường đọc được:
- Số giấy đăng ký / Số chứng nhận
- Biển số
- Họ tên chủ xe
- Địa chỉ chủ xe
- Nhãn hiệu
- Số loại
- Loại xe
- Màu sơn
- Số máy
- Số khung
- Số chỗ ngồi
- Tự trọng
- Khối lượng hàng chuyên chở
- Ngày đăng ký
- Năm sản xuất
- Dung tích xi lanh
- Công suất
- Nguồn gốc

Quan trọng: chỉ lấy giá trị đứng đúng với từng nhãn. Không nhầm ""Số:"" với ""Biển số"".
Chỉ trả JSON hợp lệ. Giữ nguyên chữ đúng như in trên giấy đăng ký.
Không tự suy đoán, không sửa lỗi, không thêm thông tin không đọc được.
Trường không đọc được phải có giá trị chuỗi rỗng.

{
  ""loai_giay_to"": ""Giấy đăng ký ô tô"",
  ""so_giay_dang_ky"": """",
  ""bien_so"": """",
  ""chu_xe"": """",
  ""dia_chi"": """",
  ""nhan_hieu"": """",
  ""so_loai"": """",
  ""loai_xe"": """",
  ""mau_son"": """",
  ""so_may"": """",
  ""so_khung"": """",
  ""so_cho_ngoi"": """",
  ""tu_trong"": """",
  ""khoi_luong_hang_cho_phep"": """",
  ""ngay_dang_ky"": """",
  ""nam_san_xuat"": """",
  ""dung_tich_xi_lanh"": """",
  ""cong_suat"": """",
  ""nguon_goc"": """"
}" },
            { "Giấy đăng ký mô tô/xe máy", @"Ảnh là Giấy đăng ký mô tô/xe máy Việt Nam hoặc bản đăng kí điện tử.
Trích xuất các trường đọc được:
- Số giấy đăng ký / Số chứng nhận
- Biển số
- Họ tên chủ xe
- Địa chỉ chủ xe
- Nhãn hiệu
- Số loại
- Loại xe
- Màu sơn
- Số máy
- Số khung
- Dung tích xi lanh
- Ngày đăng ký
- Năm sản xuất
- Nguồn gốc

Quan trọng:
- ""Số:"" là số giấy đăng ký, không phải biển số.
- ""Biển số:"" là giá trị cần ghi vào trường ""bien_so"".
- Chỉ lấy giá trị đi cùng đúng nhãn in trên giấy.

Chỉ trả JSON hợp lệ. Giữ nguyên chữ đúng như hiển thị trên tài liệu
Không tự suy đoán, không sửa lỗi, không thêm thông tin không đọc được.
Trường không đọc được phải có giá trị chuỗi rỗng.

{
  ""loai_giay_to"": ""Giấy đăng ký mô tô/xe máy"",
  ""so_giay_dang_ky"": """",
  ""bien_so"": """",
  ""chu_xe"": """",
  ""dia_chi"": """",
  ""nhan_hieu"": """",
  ""so_loai"": """",
  ""loai_xe"": """",
  ""mau_son"": """",
  ""so_may"": """",
  ""so_khung"": """",
  ""dung_tich_xi_lanh"": """",
  ""ngay_dang_ky"": """",
  ""nam_san_xuat"": """",
  ""nguon_goc"": """"
}" },
            { "Ảnh xe ô tô", @"Ảnh là ảnh chụp xe ô tô thực tế ở Việt Nam.
Hãy tìm và đọc biển số xe trong ảnh.
Trích xuất các trường sau nếu nhìn thấy:
- Biển số xe (đọc chính xác ký tự trên biển)
- Màu xe (nếu nhìn rõ)
- Nhãn hiệu xe (nếu nhận diện được)

Chỉ trả JSON hợp lệ. Giữ nguyên đúng ký tự trên biển số.
Không tự suy đoán, không sửa lỗi.
Nếu không đọc được biển số, trường ""bien_so"" phải là chuỗi rỗng.

{
  ""loai_giay_to"": ""Ảnh xe ô tô"",
  ""bien_so"": """",
  ""mau_xe"": """",
  ""nhan_hieu"": """"
}" },
            { "Không xác định", @"Hình ảnh này có thể là bất kỳ tài liệu, giấy tờ, hoặc ảnh chụp nào.
Hãy tìm và trích xuất biển số xe (license plate / registration number) trong ảnh nếu có.
Nếu tìm thấy biển số xe, hãy trả về trong trường ""bien_so"".
Nếu không có biển số xe hoặc không đọc được, trường ""bien_so"" phải là chuỗi rỗng.

Chỉ trả JSON hợp lệ:
{
  ""loai_giay_to"": ""Không xác định"",
  ""bien_so"": """"
}" }
        };

        public LLMClient(LLMConfig config, ILogger<LLMClient> logger)
        {
            _config = config;
            _logger = logger;
            _httpClient = new HttpClient();
            _httpClient.Timeout = TimeSpan.FromSeconds(_config.TimeoutSeconds);
        }

        private string ImageToDataUrl(string imagePath)
        {
            var bytes = File.ReadAllBytes(imagePath);
            var extension = Path.GetExtension(imagePath).ToLowerInvariant();
            string mimeType;
            switch (extension)
            {
                case ".png": mimeType = "image/png"; break;
                case ".webp": mimeType = "image/webp"; break;
                case ".gif": mimeType = "image/gif"; break;
                default: mimeType = "image/jpeg"; break;
            }
            var base64 = Convert.ToBase64String(bytes);
            return $"data:{mimeType};base64,{base64}";
        }

        private string ExtractJsonFromText(string text)
        {
            text = text.Trim();
            if (text.StartsWith("```"))
            {
                var lines = text.Split('\n');
                var stringBuilder = new StringBuilder();
                bool inCodeBlock = false;
                foreach (var line in lines)
                {
                    var trimmed = line.Trim();
                    if (trimmed.StartsWith("```"))
                    {
                        inCodeBlock = !inCodeBlock;
                        continue;
                    }
                    if (inCodeBlock || lines.Length <= 2)
                    {
                        stringBuilder.AppendLine(line);
                    }
                }
                if (stringBuilder.Length > 0)
                    text = stringBuilder.ToString().Trim();
            }

            var match = Regex.Match(text, @"\{[\s\S]*\}");
            if (match.Success)
            {
                return match.Value;
            }
            return text;
        }

        private async Task<string> CallApiAsync(string imageDataUrl, string prompt)
        {
            var payload = new
            {
                model = string.IsNullOrWhiteSpace(_config.Model) ? LLMConfig.DefaultModel : _config.Model,
                temperature = _config.Temperature,
                messages = new[]
                {
                    new
                    {
                        role = "user",
                        content = new object[]
                        {
                            new { type = "text", text = prompt },
                            new { type = "image_url", image_url = new { url = imageDataUrl } }
                        }
                    }
                }
            };

            var request = new HttpRequestMessage(HttpMethod.Post, $"{_config.ApiUrl}/v1/chat/completions");
            if (!string.IsNullOrEmpty(_config.ApiKey))
            {
                request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", _config.ApiKey);
            }

            var jsonPayload = JsonConvert.SerializeObject(payload);
            request.Content = new StringContent(jsonPayload, Encoding.UTF8, "application/json");

            try
            {
                var timestart = DateTime.Now;
                System.Diagnostics.Debug.WriteLine("Start request_________________________");
                var response = await _httpClient.SendAsync(request);
                var responseContent = await response.Content.ReadAsStringAsync();
                System.Diagnostics.Debug.WriteLine($"Total time request:{(DateTime.Now-timestart).TotalMilliseconds} ms");
                if (!response.IsSuccessStatusCode)
                {
                    throw new Exception($"API HTTP Error {response.StatusCode}: {responseContent}");
                }

                var jsonResponse = JObject.Parse(responseContent);
                var contentNode = jsonResponse["choices"]?[0]?["message"]?["content"];

                if (contentNode == null)
                {
                    throw new Exception("Invalid API response format.");
                }

                return contentNode.ToString();
            }
            catch (Exception ex)
            {
                _logger?.LogError(ex, "Error calling AI API");
                throw;
            }
        }

        private string GetUnifiedPrompt()
        {
            try
            {
                lock (PromptFileLock)
                {
                var appDataPath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "V3SClient", "Data", "Prompts");
                if (!Directory.Exists(appDataPath))
                {
                    Directory.CreateDirectory(appDataPath);
                }
                var promptPath = Path.Combine(appDataPath, "UnifiedPrompt.txt");
                if (File.Exists(promptPath))
                {
                    return File.ReadAllText(promptPath);
                }
                else
                {
                    // Tạo file mặc định để người dùng có thể tùy chỉnh
                    File.WriteAllText(promptPath, UNIFIED_PROMPT);
                }
                }
            }
            catch (Exception ex)
            {
                _logger?.LogWarning(ex, "Lỗi khi đọc file prompt, sử dụng prompt mặc định.");
            }
            
            return UNIFIED_PROMPT;
        }

        /// <summary>
        /// Phân loại + trích xuất biển số trong 1 lần gọi LLM duy nhất.
        /// Đây là method chính nên dùng thay cho ClassifyDocumentAsync + OcrDocumentAsync.
        /// </summary>
        public async Task<UnifiedResult> ProcessDocumentAsync(string imagePath)
        {
            var dataUrl = ImageToDataUrl(imagePath);
            var prompt = GetUnifiedPrompt();
            var rawText = await CallApiAsync(dataUrl, prompt);
            var jsonText = ExtractJsonFromText(rawText);

            try
            {
                var result = JsonConvert.DeserializeObject<UnifiedResult>(jsonText);
                
                // Validate loại giấy tờ
                var type = result?.LoaiGiayTo?.Trim() ?? "Không xác định";
                if (!VALID_TYPES.Contains(type))
                {
                    type = "Không xác định";
                }

                return new UnifiedResult
                {
                    LoaiGiayTo = type,
                    BienSo = result?.BienSo?.Trim() ?? "",
                    MauBienSo = result?.MauBienSo?.Trim() ?? "",
                    LoaiXe = result?.LoaiXe?.Trim() ?? "",
                    MauXe = result?.MauXe?.Trim() ?? "",
                    GocChup = result?.GocChup?.Trim() ?? "",
                    TenDon = result?.TenDon?.Trim() ?? "",
                    AdditionalData = result?.AdditionalData
                };
            }
            catch (Exception ex)
            {
                _logger?.LogError(ex, $"Failed to parse UnifiedResult JSON: {jsonText}");
                throw;
            }
        }

        /// <summary>
        /// Xử lý ảnh xe với metadata gợi ý từ hệ thống nhận dạng.
        /// Prompt được bổ sung thông tin biển số, góc chụp để tăng độ chính xác.
        /// </summary>
        public async Task<UnifiedResult> ProcessDocumentWithHintsAsync(string imagePath, PlateRecognitionData hints)
        {
            var dataUrl = ImageToDataUrl(imagePath);
            
            // Tạo prompt tăng cường với metadata
            string enhancedPrompt = BuildEnhancedPrompt(hints);
            
            var rawText = await CallApiAsync(dataUrl, enhancedPrompt);
            var jsonText = ExtractJsonFromText(rawText);

            try
            {
                var result = JsonConvert.DeserializeObject<UnifiedResult>(jsonText);
                
                // Validate loại giấy tờ
                var type = result?.LoaiGiayTo?.Trim() ?? "Không xác định";
                if (!VALID_TYPES.Contains(type))
                {
                    type = "Không xác định";
                }

                return new UnifiedResult
                {
                    LoaiGiayTo = type,
                    BienSo = result?.BienSo?.Trim() ?? "",
                    MauBienSo = result?.MauBienSo?.Trim() ?? "",
                    LoaiXe = result?.LoaiXe?.Trim() ?? "",
                    MauXe = result?.MauXe?.Trim() ?? "",
                    GocChup = result?.GocChup?.Trim() ?? "",
                    TenDon = result?.TenDon?.Trim() ?? "",
                    AdditionalData = result?.AdditionalData
                };
            }
            catch (Exception ex)
            {
                _logger?.LogError(ex, $"Failed to parse UnifiedResult JSON (With Hints): {jsonText}");
                throw;
            }
        }

        private string BuildEnhancedPrompt(PlateRecognitionData hints)
        {
            var sb = new StringBuilder(GetUnifiedPrompt());
            
            sb.AppendLine();
            sb.AppendLine("── THÔNG TIN BỔ SUNG TỪ HỆ THỐNG NHẬN DẠNG ──");
            
            if (!string.IsNullOrEmpty(hints?.Plate))
                sb.AppendLine($"Biển số gợi ý: \"{hints.Plate}\" (confidence: {hints.Confidence:P1})");
            
            if (!string.IsNullOrEmpty(hints?.CaptureRole))
                sb.AppendLine($"Góc chụp camera: \"{hints.CaptureRole}\"");
            
            sb.AppendLine("Hãy sử dụng thông tin trên để hỗ trợ xác nhận. " +
                "Nếu biển số gợi ý khớp với biển số nhìn thấy trong ảnh, dùng nó. " +
                "Nếu không khớp hoặc không nhìn thấy, trả về kết quả bạn đọc được.");
            
            return sb.ToString();
        }

        /// <summary>
        /// Xác nhận biển số khi confidence từ JSON thấp (< 80%).
        /// Gửi ảnh crop biển số + ảnh gốc để LLM đọc lại.
        /// </summary>
        public async Task<string> ConfirmPlateAsync(string plateImagePath, string suggestedPlate)
        {
            var dataUrl = ImageToDataUrl(plateImagePath);
            string prompt = $@"Ảnh này là ảnh crop biển số xe Việt Nam.
Hệ thống nhận dạng tự động gợi ý biển số: ""{suggestedPlate}"" nhưng độ tin cậy thấp.
Hãy đọc chính xác ký tự trên biển số trong ảnh.
Chỉ trả JSON: {{ ""bien_so"": ""..."" }}";
            
            var rawText = await CallApiAsync(dataUrl, prompt);
            var jsonText = ExtractJsonFromText(rawText);

            try
            {
                var result = JObject.Parse(jsonText);
                return result["bien_so"]?.ToString() ?? suggestedPlate;
            }
            catch (Exception ex)
            {
                _logger?.LogError(ex, $"Failed to parse ConfirmPlate JSON: {jsonText}");
                return suggestedPlate; // Fallback
            }
        }

        [Obsolete("Dùng ProcessDocumentAsync() thay thế. Method này gọi LLM riêng chỉ để phân loại.")]
        public async Task<ClassificationResult> ClassifyDocumentAsync(string imagePath)
        {
            var dataUrl = ImageToDataUrl(imagePath);
            var rawText = await CallApiAsync(dataUrl, CLASSIFICATION_PROMPT);
            var jsonText = ExtractJsonFromText(rawText);

            try
            {
                var result = JsonConvert.DeserializeObject<ClassificationResult>(jsonText);
                var type = result?.LoaiTaiLieu?.Trim() ?? "Không xác định";
                if (!VALID_TYPES.Contains(type))
                {
                    type = "Không xác định";
                }

                return new ClassificationResult
                {
                    LoaiTaiLieu = type,
                    TenDon = result?.TenDon?.Trim() ?? ""
                };
            }
            catch (Exception ex)
            {
                _logger?.LogError(ex, $"Failed to parse Classification JSON: {jsonText}");
                throw;
            }
        }

        [Obsolete("Dùng ProcessDocumentAsync() thay thế. Method này gọi LLM riêng chỉ để OCR.")]
        public async Task<ExtractedData> OcrDocumentAsync(string imagePath, string documentType)
        {
            if (!OCR_PROMPTS.ContainsKey(documentType))
            {
                documentType = "Không xác định"; // Fallback to unknown if type is missing
            }

            var prompt = OCR_PROMPTS[documentType];
            var dataUrl = ImageToDataUrl(imagePath);
            var rawText = await CallApiAsync(dataUrl, prompt);
            var jsonText = ExtractJsonFromText(rawText);

            try
            {
                var result = JsonConvert.DeserializeObject<ExtractedData>(jsonText);
                if (result != null)
                {
                    result.LoaiGiayTo = documentType;
                }
                return result;
            }
            catch (Exception ex)
            {
                _logger?.LogError(ex, $"Failed to parse OCR JSON: {jsonText}");
                throw;
            }
        }
    }
}
