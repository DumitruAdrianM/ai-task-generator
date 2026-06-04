using Microsoft.SemanticKernel;

namespace AiTaskGenerator
{
    public class OpenAiClient : IAiClient
    {
        private readonly Kernel _kernel;
        private readonly HttpClient _httpClient;

        public OpenAiClient(IConfiguration config)
        {
            var apiKey = config["OpenAI:ApiKey"]
                ?? throw new Exception("OpenAI:ApiKey not configured");
            var model = config["OpenAI:Model"]
                ?? throw new Exception("OpenAI:Model not configured");

            // 100s default would time out on large regeneration prompts.
            _httpClient = new HttpClient { Timeout = TimeSpan.FromMinutes(10) };

            var builder = Kernel.CreateBuilder();
            builder.AddOpenAIChatCompletion(model, apiKey, httpClient: _httpClient);
            _kernel = builder.Build();
        }

        public async Task<string> InvokePromptAsync(
            string promptTemplate,
            Dictionary<string, object?> args)
        {
            // Semantic Kernel handles the {{$varname}} substitution itself.
            var kargs = new KernelArguments();
            foreach (var kvp in args) kargs[kvp.Key] = kvp.Value;

            var result = await _kernel.InvokePromptAsync(promptTemplate, kargs);
            return result.ToString();
        }
    }
}
