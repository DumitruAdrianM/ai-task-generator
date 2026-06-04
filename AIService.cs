namespace AiTaskGenerator
{
    public class AiService
    {
        private readonly IAiClient _ai;

        public AiService(IAiClient ai)
        {
            _ai = ai;
        }

        public async Task<string> GenerateTasks(string input)
        {
            var prompt = @"
                You are a senior software architect.

                Given the following requirement:
                {{$input}}

                Generate a list of tasks split into:
                - Frontend
                - Backend

                For each task include:

                - title
                - type (Frontend or Backend)
                - description
                - acceptanceCriteria
                - techStack (.NET or React)
                - repoPath

                Rules:
                - techStack MUST be a string
                - techStack allowed values: .NET, React
                - type allowed values: Frontend or Backend
                - Do NOT combine values in a single string
                - repoPath must indicate where in the repo this task belongs (e.g. /backend/auth or /frontend/login)

                Return ONLY valid JSON:

                {
                  ""tasks"": [
                    {
                      ""title"": """",
                      ""type"": """",
                      ""description"": """",
                      ""acceptanceCriteria"": """",
                      ""techStack"": """",
                      ""repoPath"": """"
                    }
                  ]
                }

                Be concise and technical.
                ";

            return await _ai.InvokePromptAsync(prompt,
                new Dictionary<string, object?> { ["input"] = input });
        }
    }
}
