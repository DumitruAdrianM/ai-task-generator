namespace AiTaskGenerator
{
    public class TaskResponse
    {
        public List<TaskItem> Tasks { get; set; }
    }

    public class TaskItem
    {
        public string Id { get; set; }
        public string Title { get; set; }
        public string Type { get; set; }
        public string Status { get; set; }
        public string Assignee { get; set; }
        public string Description { get; set; }
        public string AcceptanceCriteria { get; set; }

        public string TechStack { get; set; } // ex: .NET, React
        public string RepoPath { get; set; } // ex: /backend/auth, /frontend/auth, /backend/users

        public string BranchName { get; set; }
        public string PullRequestUrl { get; set; }
    }

    public class GeneratedFile
    {
        public string Path { get; set; }
        public string Content { get; set; }
    }

    public class CodeResult
    {
        public List<GeneratedFile> Files { get; set; }
        public List<GeneratedFile> Tests { get; set; }
    }

    public class QaResult
    {
        public bool Success { get; set; }
        public string Output { get; set; }
        public string Error { get; set; }
    }
}
