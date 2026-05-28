using System.IO;

namespace AiTaskGenerator
{
    public class RepoReaderService
    {
        private readonly IConfiguration _config;

        // Folders we never want to walk into — they'd flood the context with
        // unrelated files (node_modules) or with build artifacts.
        private static readonly string[] ExcludedDirs =
        {
            "node_modules", ".git", "dist", "build", "out", "obj", "bin",
            ".next", ".cache", "coverage", "playwright-report", "test-results"
        };

        public RepoReaderService(IConfiguration config)
        {
            _config = config;
        }

        public string ReadRelevantFiles(string repoPath, string techStack)
        {
            var repos = ResolveRepos(techStack);

            var context = "";

            foreach (var repo in repos)
            {
                if (!Directory.Exists(repo))
                    continue;

                var files = EnumerateFiles(repo)
                    .Where(f => IsRelevantFile(f, techStack))
                    // priority sort: manifests first, then entry points, then the rest
                    .OrderByDescending(f =>
                    {
                        var name = Path.GetFileName(f).ToLowerInvariant();
                        if (name == "package.json") return 1000;
                        if (name is "tsconfig.json" or "playwright.config.ts"
                                  or "playwright.config.js" or "vite.config.js"
                                  or "vite.config.ts") return 800;
                        if (name is "app.js" or "app.jsx" or "app.tsx") return 500;
                        if (name is "index.js" or "index.jsx" or "main.jsx"
                                  or "main.tsx") return 400;
                        return 0;
                    })
                    .ThenBy(f => f)
                    .Take(20);

                foreach (var file in files)
                {
                    // Emit paths RELATIVE to the repo root, with forward slashes.
                    // AI copies whatever path style it sees here — showing absolute
                    // Windows paths leads it to return absolute paths for new files,
                    // which then break Playwright (interprets backslashes as regex).
                    var rel = Path.GetRelativePath(repo, file).Replace('\\', '/');
                    context += $"\n\nFILE: {rel}\n";
                    context += File.ReadAllText(file);
                }
            }

            return context;
        }

        private IEnumerable<string> EnumerateFiles(string root)
        {
            var stack = new Stack<string>();
            stack.Push(root);

            while (stack.Count > 0)
            {
                var dir = stack.Pop();

                string[] subdirs;
                try { subdirs = Directory.GetDirectories(dir); }
                catch { continue; }

                foreach (var sub in subdirs)
                {
                    var name = Path.GetFileName(sub);
                    if (ExcludedDirs.Contains(name, StringComparer.OrdinalIgnoreCase))
                        continue;
                    stack.Push(sub);
                }

                string[] files;
                try { files = Directory.GetFiles(dir); }
                catch { continue; }

                foreach (var f in files) yield return f;
            }
        }

        private List<string> ResolveRepos(string techStack)
        {
            var repos = new List<string>();

            if (techStack.Contains(".NET"))
                repos.Add(_config["Git:BackendRepoPath"]);

            if (techStack.Contains("React"))
                repos.Add(_config["Git:FrontendRepoPath"]);

            return repos;
        }

        private bool IsRelevantFile(string file, string techStack)
        {
            var name = Path.GetFileName(file).ToLowerInvariant();

            // Always-include manifests and configs (so AI sees installed deps + conventions)
            if (name is "package.json" or "tsconfig.json"
                    or "playwright.config.ts" or "playwright.config.js"
                    or "vite.config.js" or "vite.config.ts")
                return true;

            if (techStack.Contains(".NET"))
                return file.EndsWith(".cs") || file.EndsWith(".csproj");

            if (techStack.Contains("React"))
                return file.EndsWith(".jsx") || file.EndsWith(".tsx") || file.EndsWith(".js");

            if (techStack.Contains("Angular"))
                return file.EndsWith(".ts") || file.EndsWith(".html");

            return false;
        }
    }
}
