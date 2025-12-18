using System;
using System.Linq;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;
using System.Collections.Generic;

namespace AICodeExplainer.Services
{
    public class AIService
    {
        private readonly string _apiKey;
        private readonly IHttpClientFactory _httpClientFactory;

      
        private readonly string[] _preferredModels = new string[]
        {
            "gemini-1.5-preview",
            "gpt-4.1-mini",
            "gpt-4o-mini",
            "gpt-4o",
            "gpt-3.5-turbo"
        };

        private string? _selectedModel;

        public AIService(string apiKey, IHttpClientFactory httpClientFactory)
        {
            _apiKey = apiKey ?? throw new ArgumentNullException(nameof(apiKey));
            _httpClientFactory = httpClientFactory ?? throw new ArgumentNullException(nameof(httpClientFactory));
        }

        public async Task<string> ExplainCodeAsync(string userCode)
        {
            if (string.IsNullOrWhiteSpace(userCode))
                return "Hata: Gönderilecek kod boş.";

            if (string.IsNullOrEmpty(_selectedModel))
            {
                _selectedModel = await PickAvailableModelAsync();
                if (string.IsNullOrEmpty(_selectedModel))
                    return "Hata: Hesabınızda kullanılabilecek uygun bir model bulunamadı.";
            }

            var requestBody = new
            {
                model = _selectedModel,
                input = new[]
                {
                    new
                    {
                        role = "system",
                        content = "Sen bir kod açıklama asistanısın. Kodları ayrıntılı şekilde Türkçe açıkla. Kodun sonunda geliştirme önerileri sunabilirsin. Gereksiz 'İstersen...' gibi cümleleri ekleme."
                    },
                    new
                    {
                        role = "user",
                        content = $"Aşağıdaki kodu ayrıntılı, satır satır ve öğretici olarak Türkçe açıkla. Kodun sonunda programı nasıl daha iyi yapabileceğimiz ile ilgili öneriler de ekle:\n\n{userCode}"
                    }
                }
            };

            var client = _httpClientFactory.CreateClient();
            client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", _apiKey);

            var json = JsonSerializer.Serialize(requestBody);
            var httpContent = new StringContent(json, Encoding.UTF8, "application/json");

            HttpResponseMessage response;
            try
            {
                response = await client.PostAsync("https://api.openai.com/v1/responses", httpContent);
            }
            catch (Exception ex)
            {
                return $"HTTP isteği başarısız: {ex.Message}";
            }

            var responseContent = await response.Content.ReadAsStringAsync();

            if (!response.IsSuccessStatusCode)
            {
                if (responseContent.Contains("model_not_found"))
                {
                    _selectedModel = null; 
                    return $"Gemini API Error (model_not_found). Hata detay: {responseContent}";
                }

                return $"Gemini API Error: {responseContent}";
            }

            try
            {
                using var doc = JsonDocument.Parse(responseContent);

                if (doc.RootElement.TryGetProperty("output", out var outputElem) && outputElem.GetArrayLength() > 0)
                {
                    var first = outputElem[0];

                    if (first.TryGetProperty("content", out var contentElem) && contentElem.ValueKind == JsonValueKind.Array)
                    {
                        var sb = new StringBuilder();
                        foreach (var item in contentElem.EnumerateArray())
                        {
                            if (item.TryGetProperty("text", out var t))
                                sb.Append(t.GetString());
                            else if (item.TryGetProperty("type", out var type) &&
                                     type.GetString() == "output_text" &&
                                     item.TryGetProperty("text", out var t2))
                                sb.Append(t2.GetString());
                        }

                        var result = sb.ToString();
                        return TrimTrailingEmptyLines(result);
                    }

                    if (first.TryGetProperty("text", out var textDirect))
                        return TrimTrailingEmptyLines(textDirect.GetString() ?? "");
                }

                if (doc.RootElement.TryGetProperty("output_text", out var ot))
                    return TrimTrailingEmptyLines(ot.GetString() ?? "");

                return "Cevap formatı beklenenden farklı: " + responseContent;
            }
            catch (JsonException)
            {
                return "JSON parse hatası: Gelen cevap geçerli JSON değil. Raw: " + responseContent;
            }
        }

       
        private static string TrimTrailingEmptyLines(string text)
        {
            if (string.IsNullOrWhiteSpace(text))
                return text;

            var lines = text.Split(new[] { '\n' }, StringSplitOptions.None).ToList();
            while (lines.Count > 0 && string.IsNullOrWhiteSpace(lines[^1]))
                lines.RemoveAt(lines.Count - 1);

            return string.Join("\n", lines);
        }

        private async Task<string?> PickAvailableModelAsync()
        {
            var client = _httpClientFactory.CreateClient();
            client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", _apiKey);

            HttpResponseMessage resp;
            try
            {
                resp = await client.GetAsync("https://api.openai.com/v1/models");
            }
            catch (Exception)
            {
                return null;
            }

            if (!resp.IsSuccessStatusCode)
                return null;

            var body = await resp.Content.ReadAsStringAsync();
            try
            {
                using var doc = JsonDocument.Parse(body);
                if (doc.RootElement.TryGetProperty("data", out var data) && data.ValueKind == JsonValueKind.Array)
                {
                    var available = data.EnumerateArray()
                        .Select(x => x.GetProperty("id").GetString() ?? "")
                        .ToHashSet(StringComparer.OrdinalIgnoreCase);

                    foreach (var candidate in _preferredModels)
                    {
                        if (available.Contains(candidate))
                            return candidate;
                    }

                    // Eğer preferred yoksa, ilk GPT modelini al
                    return available.FirstOrDefault(s => s.StartsWith("gpt-", StringComparison.OrdinalIgnoreCase));
                }
            }
            catch (JsonException)
            {
                return null;
            }

            return null;
        }
    }
}
