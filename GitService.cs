using System.Diagnostics;

namespace AiTaskGenerator
{
    public class GitService
    {
        private readonly IConfiguration _config;

        public GitService(IConfiguration config)
        {
            _config = config;
        }

        private string GetRepoPath(string repoType)
        {
            return repoType switch
            {
                "Backend" => _config["Git:BackendRepoPath"],
                "Frontend" => _config["Git:FrontendRepoPath"],
                _ => throw new Exception("Unknown repo type")
            };
        }

        public void CreateBranch(string repoType, string branchName)
        {
            var path = GetRepoPath(repoType);

            // Discard any uncommitted changes left behind by a previous task that
            // failed mid-loop (files written but never committed).
            try { Run("git reset --hard HEAD", path); }
            catch (Exception ex)
            {
                Console.WriteLine($"git reset warning: {ex.Message}");
            }

            // Return to main so the new branch forks from a clean base, not from
            // the previous task's still-open feature branch.
            Run("git checkout main", path);

            // Best-effort: bring local main up to date with origin (in case a prior
            // PR was merged externally). Don't fail the whole flow if offline.
            try { Run("git pull --ff-only origin main", path); }
            catch (Exception ex)
            {
                Console.WriteLine($"git pull warning: {ex.Message}");
            }

            // New branch from latest main — PR diff will contain only this task.
            Run($"git checkout -b {branchName}", path);
        }

        public void Commit(string repoType, string message)
        {
            Run("git add .", GetRepoPath(repoType));
            Run($"git commit -m \"{message}\"", GetRepoPath(repoType));
        }

        public void Push(string repoType, string branchName)
        {
            Run($"git push -u origin {branchName}", GetRepoPath(repoType));
        }

        private void Run(string command, string path)
        {
            var process = new Process();

            process.StartInfo.FileName = "cmd.exe";
            process.StartInfo.Arguments = $"/c {command}";
            process.StartInfo.WorkingDirectory = path;

            process.StartInfo.RedirectStandardOutput = true;
            process.StartInfo.RedirectStandardError = true;
            process.StartInfo.UseShellExecute = false;

            process.Start();

            var output = process.StandardOutput.ReadToEnd();
            var error = process.StandardError.ReadToEnd();

            process.WaitForExit();

            if (process.ExitCode != 0)
            {
                throw new Exception($"Git error:\n{error}");
            }
        }
    }
}