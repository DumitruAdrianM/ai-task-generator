using System.Net.Http.Headers;
using System.Text;
using System.Text.RegularExpressions;
using Newtonsoft.Json;

namespace AiTaskGenerator
{

    public class GitHubService
    {
        private readonly HttpClient _http;
        private readonly IConfiguration _config;

        public GitHubService(HttpClient http, IConfiguration config)
        {
            _http = http;
            _config = config;
        }

        private (string owner, string repo, string token) GetRepoConfig(string repoType)
        {
            var token = _config["GitHub:Token"];

            var section = _config.GetSection($"GitHub:Repositories:{repoType}");

            var owner = section["Owner"];
            var repo = section["Repo"];

            if (owner == null || repo == null)
                throw new Exception($"Repo config missing for {repoType}");

            return (owner, repo, token);
        }

        public async Task<string> CreatePullRequest(string repoType, string branch, string title, string description)
        {
            var (owner, repo, token) = GetRepoConfig(repoType);

            var url = $"https://api.github.com/repos/{owner}/{repo}/pulls";

            var body = new
            {
                title,
                head = branch,
                @base = "main",
                body = description
            };

            var request = new HttpRequestMessage(HttpMethod.Post, url);
            request.Headers.Authorization =
                new AuthenticationHeaderValue("Bearer", token);

            request.Headers.Add("User-Agent", "AiTaskGenerator");

            request.Content = new StringContent(
                JsonConvert.SerializeObject(body),
                Encoding.UTF8,
                "application/json"
            );

            var response = await _http.SendAsync(request);
            var json = await response.Content.ReadAsStringAsync();

            if (!response.IsSuccessStatusCode)
                throw new Exception(json);

            dynamic result = JsonConvert.DeserializeObject(json);

            return result.html_url;
        }

        public async Task CommentOnPullRequest(
    string repoType,
    string prUrl,
    QaResult qaResult)
        {
            var (owner, repo, token) = GetRepoConfig(repoType);

            var prNumber = ExtractPrNumber(prUrl);

            var url =
                $"https://api.github.com/repos/{owner}/{repo}/issues/{prNumber}/comments";

            var body = new
            {
                body = BuildQaComment(qaResult)
            };

            var request = new HttpRequestMessage(HttpMethod.Post, url);

            request.Headers.Authorization =
                new AuthenticationHeaderValue("Bearer", token);

            request.Headers.Add("User-Agent", "AiTaskGenerator");

            request.Content = new StringContent(
                JsonConvert.SerializeObject(body),
                Encoding.UTF8,
                "application/json"
            );

            var response = await _http.SendAsync(request);

            var json = await response.Content.ReadAsStringAsync();

            if (!response.IsSuccessStatusCode)
                throw new Exception($"GitHub comment failed: {json}");
        }

        private int ExtractPrNumber(string prUrl)
        {
            var parts = prUrl.Split('/');

            return int.Parse(parts.Last());
        }

        private string BuildQaComment(QaResult qaResult)
        {
            var rawOutput = qaResult.Output ?? "";
            var rawError = qaResult.Error ?? "";
            var cleanOutput = CleanTerminalOutput(rawOutput);
            var cleanError = CleanTerminalOutput(rawError);

            var summary = ParseTestOutput(cleanOutput);

            var sb = new StringBuilder();

            if (qaResult.Success)
            {
                sb.AppendLine("## ✅ QA Passed");
                sb.AppendLine();
                if (summary.HasCounts)
                {
                    sb.AppendLine($"**{summary.Passed} passed**" +
                        (summary.Duration != null ? $" in {summary.Duration}" : ""));
                    sb.AppendLine();
                }

                if (summary.Tests.Count > 0)
                {
                    sb.AppendLine("### Tests");
                    sb.AppendLine();
                    foreach (var t in summary.Tests)
                        sb.AppendLine($"- ✓ {t}");
                    sb.AppendLine();
                }

                sb.AppendLine("Automated validation completed successfully.");
                sb.AppendLine();
            }
            else
            {
                sb.AppendLine("## ❌ QA Failed");
                sb.AppendLine();
                if (summary.HasCounts || summary.Failed > 0)
                {
                    sb.Append("**");
                    if (summary.Failed > 0) sb.Append($"{summary.Failed} failed");
                    if (summary.Failed > 0 && summary.Passed > 0) sb.Append(", ");
                    if (summary.Passed > 0) sb.Append($"{summary.Passed} passed");
                    sb.Append("**");
                    if (summary.Duration != null) sb.Append($" in {summary.Duration}");
                    sb.AppendLine();
                    sb.AppendLine();
                }

                if (!string.IsNullOrWhiteSpace(cleanError))
                {
                    sb.AppendLine("### Error");
                    sb.AppendLine();
                    sb.AppendLine("```");
                    sb.AppendLine(Truncate(cleanError, 4000));
                    sb.AppendLine("```");
                    sb.AppendLine();
                }

                sb.AppendLine("Developer review required.");
                sb.AppendLine();
            }

            // collapsible raw output
            if (!string.IsNullOrWhiteSpace(cleanOutput))
            {
                sb.AppendLine("<details>");
                sb.AppendLine("<summary>Raw test output</summary>");
                sb.AppendLine();
                sb.AppendLine("```");
                sb.AppendLine(Truncate(cleanOutput, 8000));
                sb.AppendLine("```");
                sb.AppendLine("</details>");
            }

            return sb.ToString();
        }

        // strip ANSI escape codes, normalize box-drawing artefacts, drop webpack noise
        private static string CleanTerminalOutput(string text)
        {
            if (string.IsNullOrEmpty(text)) return "";

            // ANSI CSI sequences ([ ... letter)
            text = Regex.Replace(text, @"\[[0-9;?]*[a-zA-Z]", "");

            // OEM mojibake of '║' as 'ÔÇ║' (cp850 read as Win-1252) and similar — replace with ' > '
            text = text.Replace("ÔÇ║", " > ").Replace("║", " > ");

            // other box-drawing characters: ─ │ ┌ etc — strip
            text = Regex.Replace(text, @"[─-╿]", "");

            // drop pure noise lines
            var lines = text.Split('\n')
                .Select(l => l.TrimEnd('\r', ' ', '\t'))
                .Where(l => !l.Contains("DeprecationWarning"))
                .Where(l => !l.Contains("[WebServer]"))
                .Where(l => !l.Contains("Use `node --trace-deprecation"))
                .Where(l => !string.IsNullOrWhiteSpace(l));

            return string.Join("\n", lines);
        }

        private class QaSummary
        {
            public List<string> Tests { get; } = new();
            public int Passed { get; set; }
            public int Failed { get; set; }
            public string? Duration { get; set; }
            public bool HasCounts => Passed > 0 || Failed > 0;
        }

        // Dispatcher: detect the test runner from output markers and parse accordingly.
        private static QaSummary ParseTestOutput(string clean)
        {
            // Playwright marker: '[N/M] [browser]'
            if (Regex.IsMatch(clean, @"\[\d+/\d+\]\s+\[[\w\-]+\]"))
                return ParsePlaywrightOutput(clean);

            // dotnet test marker: 'Passed!  - Failed:' / 'Failed!  - Failed:'
            //   or per-test 'Passed Foo.Bar [12 ms]'
            if (Regex.IsMatch(clean, @"(Passed|Failed)!\s+-\s+Failed:")
                || Regex.IsMatch(clean, @"^\s*(Passed|Failed|Skipped)\s+\S+\s+\[\d+\s*ms\]",
                    RegexOptions.Multiline))
                return ParseDotnetOutput(clean);

            // Unknown format — fall back to playwright (it returns empty if nothing matches).
            return ParsePlaywrightOutput(clean);
        }

        private static QaSummary ParseDotnetOutput(string clean)
        {
            var s = new QaSummary();
            var seen = new HashSet<string>();

            foreach (var rawLine in clean.Split('\n'))
            {
                var line = rawLine.Trim();

                // Per-test result (with --logger console;verbosity=normal):
                //   "Passed ai_net_startup.Tests.HealthEndpointTests.GetHealth_ReturnsOk [12 ms]"
                //   "Failed Foo.Bar.Baz [25 ms]"
                var perTestMatch = Regex.Match(line,
                    @"^(Passed|Failed|Skipped)\s+([\w\.]+)\s+\[\d+\s*ms\]");
                if (perTestMatch.Success)
                {
                    var full = perTestMatch.Groups[2].Value;
                    // Keep "ClassName.MethodName" for readability (last two segments).
                    var parts = full.Split('.');
                    var display = parts.Length >= 2
                        ? parts[^2] + "." + parts[^1]
                        : full;
                    if (seen.Add(display)) s.Tests.Add(display);
                    continue;
                }

                // Summary line:
                //   "Failed:     0, Passed:     1, Skipped:     0, Total:     1, Duration: 528 ms"
                var passMatch = Regex.Match(line, @"Passed:\s*(\d+)");
                if (passMatch.Success) s.Passed = int.Parse(passMatch.Groups[1].Value);

                var failMatch = Regex.Match(line, @"Failed:\s*(\d+)");
                if (failMatch.Success) s.Failed = int.Parse(failMatch.Groups[1].Value);

                var durMatch = Regex.Match(line,
                    @"Duration:\s*([\d.]+\s*(?:ms|s))");
                if (durMatch.Success) s.Duration = durMatch.Groups[1].Value;
            }

            return s;
        }

        private static QaSummary ParsePlaywrightOutput(string clean)
        {
            var s = new QaSummary();
            var seen = new HashSet<string>();

            foreach (var rawLine in clean.Split('\n'))
            {
                var line = rawLine.Trim();

                // [N/M] [browser]  path:line:col > Describe > Test name
                var testMatch = Regex.Match(line,
                    @"^\[\d+/\d+\](?:\s+\(retries\))?\s+\[[\w\-]+\]\s+(.+?)$");
                if (testMatch.Success)
                {
                    var rest = testMatch.Groups[1].Value;
                    // peel off "filename:line:col > " if present
                    var afterPath = Regex.Replace(rest,
                        @"^\S+\.spec\.\w+:\d+:\d+\s*>\s*", "");
                    var name = afterPath.Trim().TrimEnd('>').Trim();
                    if (!string.IsNullOrWhiteSpace(name) && seen.Add(name))
                        s.Tests.Add(name);
                    continue;
                }

                // summary line:  " 5 passed (13.0s)"  or  "1 failed"
                var passMatch = Regex.Match(line, @"^\s*(\d+)\s+passed(?:\s+\(([^)]+)\))?");
                if (passMatch.Success)
                {
                    s.Passed = int.Parse(passMatch.Groups[1].Value);
                    if (passMatch.Groups[2].Success) s.Duration = passMatch.Groups[2].Value;
                    continue;
                }
                var failMatch = Regex.Match(line, @"^\s*(\d+)\s+failed");
                if (failMatch.Success)
                    s.Failed = int.Parse(failMatch.Groups[1].Value);
            }

            return s;
        }

        private static string Truncate(string s, int max)
        {
            if (s.Length <= max) return s;
            return s.Substring(0, max) + "\n... (truncated)";
        }
    }
}
