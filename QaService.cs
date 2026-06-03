using System.Diagnostics;
using System.Text;

namespace AiTaskGenerator
{
    public class QaService
    {
        private readonly IConfiguration _config;

        public QaService(IConfiguration config)
        {
            _config = config;
        }

        //public async Task<QaResult> Run(TaskItem task)
        //{
        //    if (task.TechStack.Contains("React"))
        //        return RunPlaywright();

        //    if (task.TechStack.Contains(".NET"))
        //        return RunDotnetTests();

        //    throw new Exception("Unsupported tech stack");
        //}

        //private QaResult RunPlaywright()
        //{
        //    KillProcessOnPort(3000);  // <— înainte de fiecare rulare

        //    var repo = _config["Git:FrontendRepoPath"];
        //    return Execute(
        //        "npx playwright test --workers=1 --reporter=line --max-failures=1",
        //        repo,
        //        extraEnv: new() { ["CI"] = "true" }
        //    );
        //}

        public async Task<QaResult> Run(TaskItem task, List<GeneratedFile> generatedTests)
        {
            if (task.TechStack.Contains("React"))
                return RunPlaywright(generatedTests);
            if (task.TechStack.Contains(".NET"))
                return RunDotnetTests(generatedTests);
            throw new Exception("Unsupported tech stack");
        }

        private QaResult RunDotnetTests(List<GeneratedFile> tests)
        {
            if (tests == null || tests.Count == 0)
            {
                return new QaResult
                {
                    Success = false,
                    Output = "",
                    Error = "AI returned no tests — cannot validate this iteration. " +
                            "A regenerated attempt must include at least one test file."
                };
            }

            var repo = _config["Git:BackendRepoPath"];

            // Extract test class names from generated file paths.
            // xUnit's --filter does a substring match on FullyQualifiedName, so the
            // class name alone matches any test in that class regardless of namespace.
            var classNames = tests
                .Select(t => Path.GetFileNameWithoutExtension(
                    FileWriterService.NormalizePath(t.Path, repo)))
                .Where(n => !string.IsNullOrWhiteSpace(n))
                .Distinct()
                .ToList();

            // OR-combine class filters with the pipe operator (vstest syntax).
            var filter = string.Join("|",
                classNames.Select(n => $"FullyQualifiedName~{n}"));

            // --logger console;verbosity=normal prints per-test names that the
            // PR-comment parser uses to build the list of tests.
            var cmd =
                $"dotnet test --filter \"{filter}\" " +
                "--logger \"console;verbosity=normal\" --nologo";

            return Execute(cmd, repo);
        }

        private QaResult RunPlaywright(List<GeneratedFile> tests)
        {
            if (tests == null || tests.Count == 0)
            {
                return new QaResult
                {
                    Success = false,
                    Output = "",
                    Error = "AI returned no tests — cannot validate this iteration. " +
                            "A regenerated attempt must include at least one test file."
                };
            }

            var repo = _config["Git:FrontendRepoPath"];

            // Normalize paths: AI sometimes returns absolute Windows paths
            // (D:\...\ai-react-app\e2e\login.spec.js) which Playwright CLI treats as
            // regex with broken backslash-escapes and reports 'No tests found'.
            var paths = string.Join(" ",
                tests.Select(t =>
                {
                    var rel = FileWriterService.NormalizePath(t.Path, repo);
                    return $"\"{rel}\"";
                }));

            var cmd =
                $"npx playwright test {paths} " +
                "--project=chromium --workers=1 --reporter=line --max-failures=1";

            return Execute(cmd, repo, new() { ["CI"] = "true" });
        }

        //private QaResult RunDotnetTests()
        //{
        //    var repo = _config["Git:BackendRepoPath"];

        //    return Execute(
        //        "dotnet test",
        //        repo
        //    );
        //}

        //public string BuildQaFeedback(QaResult qa)
        //{
        //    var text = $"{qa.Output}\n{qa.Error}";

        //    if (text.Length > 4000)
        //        text = text.Substring(0, 4000);

        //    return text;
        //}

        public string BuildQaFeedback(QaResult qa)
        {
            var text = $"{qa.Output}\n{qa.Error}";

            // strip ANSI escapes
            text = System.Text.RegularExpressions.Regex.Replace(
                text, @"\u001b\[[0-9;]*[a-zA-Z]", "");

            // drop webpack deprecation noise
            text = string.Join("\n",
                text.Split('\n').Where(l =>
                    !l.Contains("DeprecationWarning") &&
                    !l.Contains("[WebServer]") &&
                    !string.IsNullOrWhiteSpace(l)));

            if (text.Length > 4000)
                text = text.Substring(text.Length - 4000); // ține CODA, nu HEAD-ul

            return text;
        }

        //private QaResult Execute(string command, string path)
        //{
        //    var process = new Process();

        //    process.StartInfo.FileName = "cmd.exe";
        //    process.StartInfo.Arguments = $"/c {command}";
        //    process.StartInfo.WorkingDirectory = path;
        //    process.StartInfo.RedirectStandardOutput = true;
        //    process.StartInfo.RedirectStandardError = true;
        //    process.StartInfo.UseShellExecute = false;

        //    process.Start();

        //    var output = process.StandardOutput.ReadToEnd();
        //    var error = process.StandardError.ReadToEnd();

        //    process.WaitForExit();

        //    return new QaResult
        //    {
        //        Success = process.ExitCode == 0,
        //        Output = output,
        //        Error = error
        //    };
        //}

        private QaResult Execute(
    string command,
    string path,
    Dictionary<string, string>? extraEnv = null)
        {
            var process = new Process();

            process.StartInfo.FileName = "cmd.exe";
            process.StartInfo.Arguments = $"/c {command}";
            process.StartInfo.WorkingDirectory = path;

            process.StartInfo.RedirectStandardOutput = true;
            process.StartInfo.RedirectStandardError = true;
            process.StartInfo.UseShellExecute = false;
            process.StartInfo.CreateNoWindow = true;
            // Playwright outputs UTF-8 (box drawings, checkmarks). Without this, .NET
            // falls back to the OEM code page on Windows and we get 'ÔÇ║' mojibake.
            process.StartInfo.StandardOutputEncoding = Encoding.UTF8;
            process.StartInfo.StandardErrorEncoding = Encoding.UTF8;

            if (extraEnv != null)
                foreach (var kv in extraEnv)
                    process.StartInfo.EnvironmentVariables[kv.Key] = kv.Value;

            var output = new StringBuilder();
            var error = new StringBuilder();

            process.OutputDataReceived += (_, e) =>
            { if (e.Data != null) output.AppendLine(e.Data); };
            process.ErrorDataReceived += (_, e) =>
            { if (e.Data != null) error.AppendLine(e.Data); };

            process.Start();
            process.BeginOutputReadLine();
            process.BeginErrorReadLine();

            const int timeoutMs = 10 * 60 * 1000; // 10 min

            if (!process.WaitForExit(timeoutMs))
            {
                try { process.Kill(entireProcessTree: true); KillProcessOnPort(3000); } catch { }

                return new QaResult
                {
                    Success = false,
                    Output = output.ToString(),
                    Error = error.ToString() + "\nQA timed out after 10 minutes."
                };
            }

            // a doua chemare așteaptă DOAR drain-ul stream-urilor,
            // după ce știm că procesul a ieșit
            process.WaitForExit();

            return new QaResult
            {
                Success = process.ExitCode == 0,
                Output = output.ToString(),
                Error = error.ToString()
            };
        }

        private void KillProcessOnPort(int port)
        {
            var psi = new ProcessStartInfo("cmd.exe",
                $"/c for /f \"tokens=5\" %a in ('netstat -ano ^| findstr :{port} ^| findstr LISTENING') do taskkill /F /PID %a")
            {
                UseShellExecute = false,
                CreateNoWindow = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true
            };
            using var p = Process.Start(psi);
            p.WaitForExit(5000);
        }
    }
}