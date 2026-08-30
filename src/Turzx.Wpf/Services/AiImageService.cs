using System;
using System.Collections.Generic;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Web.Script.Serialization;
using Turzx.Desktop.Models;

namespace Turzx.Desktop.Services
{
    internal sealed class AiImageService : IDisposable
    {
        private readonly HttpClient _client = new HttpClient
        {
            Timeout = TimeSpan.FromMinutes(3)
        };
        private readonly JavaScriptSerializer _json = new JavaScriptSerializer
        {
            MaxJsonLength = int.MaxValue
        };

        public async Task<byte[]> GenerateAsync(AiImageSettings settings, string prompt,
            string quality, CancellationToken token)
        {
            if (settings == null) throw new ArgumentNullException("settings");
            if (string.IsNullOrWhiteSpace(settings.Endpoint))
                throw new InvalidOperationException("请先在设置中填写 API 地址。");
            if (string.IsNullOrWhiteSpace(settings.ApiKey))
                throw new InvalidOperationException("请先在设置中填写 API 密钥。");
            if (string.IsNullOrWhiteSpace(settings.Model))
                throw new InvalidOperationException("请先在设置中填写图像模型。");

            bool dashScopeNative = IsDashScopeNative(settings);
            string endpoint = ResolveEndpoint(settings, dashScopeNative);
            Dictionary<string, object> requestBody = dashScopeNative ?
                CreateDashScopeRequest(settings.Model.Trim(), prompt, quality) :
                CreateOpenAiRequest(settings.Model.Trim(), prompt, quality);

            using (HttpRequestMessage request = new HttpRequestMessage(
                HttpMethod.Post, endpoint))
            {
                request.Headers.Authorization = new AuthenticationHeaderValue(
                    "Bearer", settings.ApiKey.Trim());
                request.Content = new StringContent(_json.Serialize(requestBody),
                    Encoding.UTF8, "application/json");
                using (HttpResponseMessage response = await _client.SendAsync(request, token))
                {
                    string json = await response.Content.ReadAsStringAsync();
                    if (!response.IsSuccessStatusCode)
                    {
                        if (dashScopeNative && response.StatusCode ==
                            System.Net.HttpStatusCode.Unauthorized)
                            throw new InvalidOperationException(
                                "百炼认证失败：API Key 无效，或 Key 与接口地域/业务空间不一致。" +
                                "请在百炼控制台复制该模型所在业务空间的 API Key 和专属域名。");
                        if (response.StatusCode == System.Net.HttpStatusCode.NotFound)
                            throw new InvalidOperationException(
                                "接口路径不存在（404）：请检查 API 地址。当前请求地址：" + endpoint);
                        throw new InvalidOperationException("API 返回 " +
                            (int)response.StatusCode + "：" + ExtractError(json));
                    }
                    return await ReadImageAsync(json, dashScopeNative, token);
                }
            }
        }

        private static Dictionary<string, object> CreateOpenAiRequest(string model,
            string prompt, string quality)
        {
            Dictionary<string, object> body = new Dictionary<string, object>
            {
                { "model", model },
                { "prompt", prompt },
                { "n", 1 },
                { "size", "1536x1024" }
            };
            if (!string.IsNullOrWhiteSpace(quality)) body["quality"] = quality;
            return body;
        }

        private static Dictionary<string, object> CreateDashScopeRequest(string model,
            string prompt, string quality)
        {
            Dictionary<string, object> content = new Dictionary<string, object>
            {
                { "text", prompt }
            };
            Dictionary<string, object> message = new Dictionary<string, object>
            {
                { "role", "user" },
                { "content", new object[] { content } }
            };
            Dictionary<string, object> input = new Dictionary<string, object>
            {
                { "messages", new object[] { message } }
            };
            bool highQuality = string.Equals(quality, "high",
                StringComparison.OrdinalIgnoreCase);
            Dictionary<string, object> parameters = new Dictionary<string, object>
            {
                { "prompt_extend", true },
                { "prompt_extend_mode", highQuality ? "agent" : "direct" },
                { "enable_thinking", highQuality },
                { "n", 1 },
                { "size", "960*320" },
                { "negative_prompt", "文字、字母、数字、百分号、单位、日期、时间、图标、进度条、刻度、曲线、柱状图、波形、数据点、仪表读数、水印" },
                { "watermark", false }
            };
            return new Dictionary<string, object>
            {
                { "model", model },
                { "input", input },
                { "parameters", parameters }
            };
        }

        private static bool IsDashScopeNative(AiImageSettings settings)
        {
            Uri uri;
            if (!Uri.TryCreate(settings.Endpoint, UriKind.Absolute, out uri)) return false;
            string host = uri.Host ?? string.Empty;
            bool dashScopeHost = host.IndexOf("dashscope", StringComparison.OrdinalIgnoreCase) >= 0 ||
                host.EndsWith(".maas.aliyuncs.com", StringComparison.OrdinalIgnoreCase);
            return dashScopeHost && (settings.Model ?? string.Empty).StartsWith(
                "qwen-image", StringComparison.OrdinalIgnoreCase);
        }

        private static string ResolveEndpoint(AiImageSettings settings, bool dashScopeNative)
        {
            string endpoint = settings.Endpoint.Trim().TrimEnd('/');
            Uri uri;
            if (!Uri.TryCreate(endpoint, UriKind.Absolute, out uri)) return endpoint;
            if (dashScopeNative)
            {
                if (uri.AbsolutePath.IndexOf("/api/v1/services/aigc/",
                    StringComparison.OrdinalIgnoreCase) >= 0) return endpoint;
                return uri.GetLeftPart(UriPartial.Authority) +
                    "/api/v1/services/aigc/multimodal-generation/generation";
            }
            if (uri.AbsolutePath.EndsWith("/v1", StringComparison.OrdinalIgnoreCase))
                return endpoint + "/images/generations";
            return endpoint;
        }

        private async Task<byte[]> ReadImageAsync(string json, bool dashScopeNative,
            CancellationToken token)
        {
            Dictionary<string, object> root = _json.DeserializeObject(json) as
                Dictionary<string, object>;
            if (dashScopeNative)
            {
                string dashScopeUrl = ReadDashScopeImageUrl(root);
                if (!string.IsNullOrWhiteSpace(dashScopeUrl))
                {
                    token.ThrowIfCancellationRequested();
                    return await _client.GetByteArrayAsync(dashScopeUrl);
                }
                throw new InvalidOperationException("DashScope 响应中没有图片 URL。");
            }
            object rawData;
            object[] data = root != null && root.TryGetValue("data", out rawData) ?
                rawData as object[] : null;
            Dictionary<string, object> image = data != null && data.Length > 0 ?
                data[0] as Dictionary<string, object> : null;
            if (image == null)
                throw new InvalidOperationException("API 响应中没有图片数据。");

            object rawBase64;
            if (image.TryGetValue("b64_json", out rawBase64) && rawBase64 != null)
                return Convert.FromBase64String(Convert.ToString(rawBase64));

            object rawUrl;
            if (image.TryGetValue("url", out rawUrl) && rawUrl != null)
            {
                token.ThrowIfCancellationRequested();
                return await _client.GetByteArrayAsync(Convert.ToString(rawUrl));
            }
            throw new InvalidOperationException("API 响应不包含 b64_json 或图片 URL。");
        }

        private static string ReadDashScopeImageUrl(Dictionary<string, object> root)
        {
            if (root == null) return null;
            object rawOutput;
            Dictionary<string, object> output = root.TryGetValue("output", out rawOutput) ?
                rawOutput as Dictionary<string, object> : null;
            if (output == null) return null;

            object rawChoices;
            object[] choices = output.TryGetValue("choices", out rawChoices) ?
                rawChoices as object[] : null;
            Dictionary<string, object> choice = choices != null && choices.Length > 0 ?
                choices[0] as Dictionary<string, object> : null;
            object rawMessage;
            Dictionary<string, object> message = choice != null &&
                choice.TryGetValue("message", out rawMessage) ?
                rawMessage as Dictionary<string, object> : null;
            object rawContent;
            object[] content = message != null && message.TryGetValue("content", out rawContent) ?
                rawContent as object[] : null;
            Dictionary<string, object> image = content != null && content.Length > 0 ?
                content[0] as Dictionary<string, object> : null;
            object rawImage;
            if (image != null && image.TryGetValue("image", out rawImage))
                return Convert.ToString(rawImage);

            object rawResults;
            object[] results = output.TryGetValue("results", out rawResults) ?
                rawResults as object[] : null;
            Dictionary<string, object> result = results != null && results.Length > 0 ?
                results[0] as Dictionary<string, object> : null;
            object rawUrl;
            return result != null && result.TryGetValue("url", out rawUrl) ?
                Convert.ToString(rawUrl) : null;
        }

        private string ExtractError(string json)
        {
            try
            {
                Dictionary<string, object> root = _json.DeserializeObject(json) as
                    Dictionary<string, object>;
                object rawError;
                Dictionary<string, object> error = root != null &&
                    root.TryGetValue("error", out rawError) ?
                    rawError as Dictionary<string, object> : null;
                object rawMessage;
                if (error != null && error.TryGetValue("message", out rawMessage))
                    return Convert.ToString(rawMessage);
                if (root != null && root.TryGetValue("message", out rawMessage))
                    return Convert.ToString(rawMessage);
            }
            catch { }
            return json == null || json.Length <= 300 ? json : json.Substring(0, 300);
        }

        public void Dispose()
        {
            _client.Dispose();
        }
    }
}
