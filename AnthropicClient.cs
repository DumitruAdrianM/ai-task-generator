using System.Text;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace AiTaskGenerator
{
    /// <summary>
    /// Thin wrapper around the Anthropic Messages API. Does manual {{$key}}
    /// substitution since we don't go through Semantic Kernel here.
    /// </summary>
    public class AnthropicClient : IAiClient
    {
        private const string Endpoint = "https://api.anthropic.com/v1/messages";
        private const string ApiVersion = "2023-06-01";
        private const int DefaultMaxTokens = 16384;

        private readonly HttpClient _http;
        private readonly string _apiKey;
        private readonly string _model;

        public AnthropicClient(IConfiguration config)
        {
            _apiKey = config["Anthropic:ApiKey"]
                ?? throw new Exception("Anthropic:ApiKey not configured");
            _model = config["Anthropic:Model"]
                ?? "claude-sonnet-4-5-20250929";

            _http = new HttpClient { Timeout = TimeSpan.FromMinutes(10) };
        }

        public async Task<string> InvokePromptAsync(
            string promptTemplate,
            Dictionary<string, object?> args)
        {
            var prompt = Substitute(promptTemplate, args);

            var body = new
            {
                model = _model,
                max_tokens = DefaultMaxTokens,
                messages = new[]
                {
                    new { role = "user", content = prompt }
                }
            };

            var req = new HttpRequestMessage(HttpMethod.Post, Endpoint);
            req.Headers.Add("x-api-key", _apiKey);
            req.Headers.Add("anthropic-version", ApiVersion);
            req.Content = new StringContent(
                JsonConvert.SerializeObject(body),
                Encoding.UTF8,
                "application/json");

            var resp = await _http.SendAsync(req);
            var json = await resp.Content.ReadAsStringAsync();

            if (!resp.IsSuccessStatusCode)
                throw new Exception(
                    $"Anthropic API error {(int)resp.StatusCode}: {json}");

            return ExtractText(json);
        }

        private static string Substitute(
            string template,
            Dictionary<string, object?> args)
        {
            // Semantic-Kernel-style placeholders: {{$varname}}.
            // Simple literal replacement — no regex, so special chars in values
            // (curly braces, dollar signs, etc.) pass through cleanly.
            foreach (var kvp in args)
            {
                var token = "{{$" + kvp.Key + "}}";
                template = template.Replace(token, kvp.Value?.ToString() ?? "");
            }
            return template;
        }

        private static string ExtractText(string responseJson)
        {
            var data = JObject.Parse(responseJson);
            var content = data["content"] as JArray;
            if (content == null || content.Count == 0)
                throw new Exception(
                    $"Anthropic response missing 'content': {responseJson}");

            // Concatenate all text blocks (usually one). Skip non-text blocks
            // (e.g. tool_use, thinking) if any appear.
            var sb = new StringBuilder();
            foreach (var block in content)
            {
                if ((string?)block["type"] == "text")
                    sb.Append((string?)block["text"]);
            }

            var text = sb.ToString();
            if (string.IsNullOrEmpty(text))
                throw new Exception(
                    $"Anthropic response had no text blocks: {responseJson}");

            return text;
        }
    }
}
