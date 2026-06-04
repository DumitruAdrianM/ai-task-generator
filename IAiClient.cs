namespace AiTaskGenerator
{
    /// <summary>
    /// Abstracts AI text generation so the orchestrator can switch between
    /// providers (OpenAI, Anthropic, ...) without touching call sites.
    ///
    /// Prompt templates use Semantic-Kernel-style placeholders: {{$varname}}.
    /// Each implementation is responsible for substituting them with values
    /// from the args dictionary before sending to its provider.
    /// </summary>
    public interface IAiClient
    {
        Task<string> InvokePromptAsync(
            string promptTemplate,
            Dictionary<string, object?> args);
    }
}
