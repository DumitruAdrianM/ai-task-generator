using Microsoft.SemanticKernel;

namespace AiTaskGenerator
{
    public class QaGenerationService
    {
        private readonly Kernel _kernel;

        public QaGenerationService(Kernel kernel)
        {
            _kernel = kernel;
        }

        public async Task<string> GenerateTests(TaskItem task)
        {
            var prompt = @"
            Generate Playwright tests for:

            TITLE:
            {{$title}}

            DESCRIPTION:
            {{$description}}

            Return ONLY test code.
            ";

            var args = new KernelArguments
            {
                ["title"] = task.Title,
                ["description"] = task.Description
            };

            var result = await _kernel.InvokePromptAsync(prompt, args);

            return result.ToString();
        }
    }
}