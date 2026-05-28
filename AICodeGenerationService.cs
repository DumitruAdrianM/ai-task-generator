using Microsoft.SemanticKernel;
using Newtonsoft.Json;

namespace AiTaskGenerator
{
    public class CodeGenerationService
    {
        private readonly Kernel _kernel;
        private readonly HttpClient _openAiHttpClient;

        public CodeGenerationService(IConfiguration config)
        {
            var apiKey = config["OpenAI:ApiKey"];
            var model = config["OpenAI:Model"];

            // The default HttpClient used by Semantic Kernel has a 100s timeout.
            // Regeneration prompts now include previousAttempt + repoContext + qaFeedback,
            // which for gpt-4.1 can easily exceed 100s end-to-end and trigger a
            // SocketException ('I/O operation aborted'). Use a longer-lived client.
            _openAiHttpClient = new HttpClient
            {
                Timeout = TimeSpan.FromMinutes(10)
            };

            var builder = Kernel.CreateBuilder();
            builder.AddOpenAIChatCompletion(model, apiKey, httpClient: _openAiHttpClient);

            _kernel = builder.Build();
        }

        public async Task<string> GenerateCode(TaskItem task, string repoContext)
        {
            var prompt = @"
            You are a senior software engineer and QA engineer.

            You MUST generate code ONLY for the specified TECH STACK.

            TECH STACK:
            {{$techStack}}

            STRICT RULES:
            - If TECH STACK is .NET → generate ONLY C# (.cs) production files and xUnit tests
            - If TECH STACK is React → generate ONLY React files (.jsx/.tsx/.css) and Playwright tests
            - If TECH STACK is Angular → generate Angular files and Playwright tests
            - NEVER mix technologies
            - Follow repo context strictly

            TASK:

            TITLE:
            {{$title}}

            DESCRIPTION:
            {{$description}}

            ACCEPTANCE CRITERIA:
            {{$criteria}}

            REPO CONTEXT:
            {{$repo}}

            Return ONLY valid JSON:

            {
              ""files"": [
                {
                  ""path"": """",
                  ""content"": """"
                }
              ],
              ""tests"": [
                {
                  ""path"": """",
                  ""content"": """"
                }
              ]
            }

            TEST RULES:
            - React → generate Playwright E2E tests
            - .NET → generate xUnit integration/unit tests
            - Tests must validate acceptance criteria
            - Tests must compile and run

            LOADING STATE TEST PATTERN (React/Playwright — use this EXACT ordering for any
            assertion about a transient UI state such as 'loading', 'submitting', 'disabled
            during fetch', etc.):

              // 1. Mock the API with a delay so the loading state is observable
              await page.route('**/api/login', async route => {
                await new Promise(r => setTimeout(r, 400));
                await route.fulfill({ status: 200, body: JSON.stringify({ token: 'x' }) });
              });

              // 2. Fire the click but DO NOT await the fetch response yet
              const clickPromise = page.click('button[type=\""submit\""]');

              // 3. While the mock is still sleeping, assert the loading UI
              await expect(page.getByRole('button', { name: /logging in/i })).toBeDisabled();

              // 4. NOW wait for the click action to settle
              await clickPromise;

              // 5. Assert the post-loading state
              await expect(page.getByRole('button', { name: /^login$/i })).toBeEnabled();

            ANTI-PATTERN — DO NOT WRITE THIS:
              const [response] = await Promise.all([
                page.waitForResponse('/api/login'),    // waits for response FIRST
                page.click('button[type=\""submit\""]'),
              ]);
              await expect(page.getByRole('button', { name: /logging in/i })).toBeDisabled();
              // ← By the time we get here, the response has resolved, setLoading(false) has
              //   already fired, the 'Logging in...' button no longer exists.
              //   Assertion will fail with 'element not found' even though the component is correct.

            PROJECT LANGUAGE DETECTION (do this FIRST, before generating any new file):
            - Inspect REPO CONTEXT for TypeScript indicators:
              * tsconfig.json present → TypeScript project
              * 'typescript' in package.json dependencies or devDependencies → TypeScript
              * Existing .ts/.tsx files in src/ → TypeScript
              * Existing App.tsx (not App.js) → TypeScript
            - All NEW files MUST match the project language:
              * TypeScript project: use .ts for utilities/hooks/contexts,
                .tsx for components that render JSX
              * JavaScript project: use .js or .jsx (whichever the existing files use)
            - For .ts/.tsx files, ALWAYS write proper type annotations:
              * Props: type Props = { ... } or interface Props { ... }
              * State: useState<Type>(initial)
              * Function signatures with explicit parameter and return types
              * Avoid `any` unless truly unavoidable
            - NEVER mix languages: a TypeScript project must NOT receive new .js
              files; a JavaScript project must NOT receive new .ts files. The
              orchestrator will reject mismatched files before they reach Playwright.

            REPO CONVENTIONS (critical — wrong extensions cause silent failures):
            - Inspect REPO CONTEXT for existing file extensions before naming yours.
            - If the existing entrypoint is App.js, your replacement MUST also be named App.js
              (NOT App.jsx). Likewise: index.js stays index.js, App.ts stays App.ts.
            - Webpack/CRA resolves .js BEFORE .jsx for the same module name. Writing App.jsx
              while App.js exists means webpack keeps loading the old App.js and your new
              file is silently ignored. The page will not update and tests will fail with
              'element not found'.
            - Same rule for .ts vs .tsx, .mjs vs .cjs.
            - If you must rename a file's extension, mention it explicitly so the orchestrator
              can clean up — but prefer matching the existing convention.

            MOUNTING / ROUTING RULES (read this before creating any new page component):
            - The dev server (webpack-dev-server / Vite) serves the SAME index.html for
              every URL via historyApiFallback. Visiting /login will load whatever
              App.js renders by default — there is NO automatic routing.
            - If your task involves a URL like /login, /signup, /dashboard, you MUST
              update src/App.js (or the existing entry component) to render the right
              child component based on window.location.pathname. Otherwise the new
              component is just a file on disk that nothing imports.
            - Creating Login.jsx and LoginForm.jsx is NOT enough. Without an updated
              App.js, page.goto('/login') will render the OLD App content (e.g. the
              CRA 'Learn React' page) and the test will fail with 'element not found'
              for every label/heading on the new page.
            - If react-router-dom is in package.json, use it. Otherwise implement
              manual routing in App.js:
                import Login from './pages/Login';
                function App() {
                  const [path, setPath] = React.useState(window.location.pathname);
                  React.useEffect(() => {
                    const h = () => setPath(window.location.pathname);
                    window.addEventListener('popstate', h);
                    return () => window.removeEventListener('popstate', h);
                  }, []);
                  if (path === '/login') return <Login />;
                  return <DefaultHome />;
                }
            - WHENEVER you create a new page component, ALWAYS also include an
              updated App.js in the 'files' array that mounts it at the right URL.

            DEPENDENCY RULES (read package.json in REPO CONTEXT before importing):
            - You may ONLY import packages already listed under 'dependencies' or
              'devDependencies' in package.json. The orchestrator does NOT install
              new packages.
            - Common packages that often LOOK available but are NOT in a default CRA
              repo: react-router-dom, axios, redux, @reduxjs/toolkit, react-query,
              zustand, formik, react-hook-form, styled-components. CHECK package.json
              before using any of these.
            - If the task needs functionality that requires a missing package, do ONE of:
                (a) implement it with what's available — plain fetch instead of axios,
                    window.history + conditional rendering instead of react-router-dom,
                    useState/useReducer instead of redux;
                (b) if (a) is impossible, return a JSON response with an explanatory
                    error in the first file's content, e.g.
                    { ""files"": [{ ""path"": ""ERROR.md"",
                      ""content"": ""Task requires package X which is not installed."" }],
                      ""tests"": [] }
            - Importing a missing package causes 'Module not found' at compile time.
              The dev server then serves an error page, tests fail with 'element not
              found', and the failure message will mislead you into debugging your
              component code. Do NOT trust 'element not found' errors without first
              verifying every import resolves to a package in package.json.

            FILE PATH RULES:
            - The 'path' field in each generated file MUST be RELATIVE to the repo root,
              using forward slashes. Examples: 'src/App.js', 'e2e/login.spec.js',
              'src/components/LoginForm.jsx'.
            - NEVER return absolute paths like 'D:\\Proiecte\\...\\src\\App.js' or
              'C:/Users/.../App.js'. The orchestrator will reject them and Playwright
              will silently report 'No tests found' because backslashes inside Windows
              paths are interpreted as broken regex escapes.
            - The REPO CONTEXT above shows file paths relative to the repo root. Match
              that style exactly.

            GENERAL RULES:
            - no markdown
            - no explanations
            - follow existing architecture (this includes file extensions, naming, import style)
            - paths must match tech stack
            - multiple files allowed
            ";

            var args = new KernelArguments
            {
                ["title"] = task.Title,
                ["description"] = task.Description,
                ["criteria"] = task.AcceptanceCriteria,
                ["techStack"] = task.TechStack,
                ["repo"] = repoContext
            };

            var result = await _kernel.InvokePromptAsync(prompt, args);

            return result.ToString();
        }

        //    public async Task<string> RegenerateCode(
        //TaskItem task,
        //string repoContext,
        //string qaFeedback)
        //    {
        //        var prompt = @"
        //            Previous implementation failed QA.

        //            TASK:
        //            {{$title}}

        //            DESCRIPTION:
        //            {{$description}}

        //            FAILED TEST OUTPUT:
        //            {{$qaFeedback}}

        //            REPO CONTEXT:
        //            {{$repo}}

        //            Fix the implementation.

        //            Return ONLY valid JSON:
        //            {
        //              ""files"": [],
        //              ""tests"": []
        //            }
        //            ";

        //        var args = new KernelArguments
        //        {
        //            ["title"] = task.Title,
        //            ["description"] = task.Description,
        //            ["qaFeedback"] = qaFeedback,
        //            ["repo"] = repoContext
        //        };

        //        var result = await _kernel.InvokePromptAsync(prompt, args);

        //        return result.ToString();
        //    }

        public async Task<string> RegenerateCode(
            TaskItem task,
            string repoContext,
            string qaFeedback,
            CodeResult previousAttempt)
        {
            var prevJson = previousAttempt == null
                ? "(no previous attempt — last generation failed to parse as JSON)"
                : JsonConvert.SerializeObject(previousAttempt, Formatting.Indented);

            var prompt = @"
                Previous implementation failed QA. You must produce a DIFFERENT attempt
                that fixes the actual root cause.

                TASK: {{$title}}
                DESCRIPTION: {{$description}}

                PREVIOUS ATTEMPT (this is what you produced last time — analyze why it failed,
                do NOT submit the same code again):
                {{$previous}}

                FAILED TEST OUTPUT:
                {{$qaFeedback}}

                REPO CONTEXT:
                {{$repo}}

                NAVIGATION + INIT SCRIPT INTERACTION (Playwright gotcha):
                - page.addInitScript() registered in beforeEach runs on EVERY new page
                  load, including navigations YOUR COMPONENT triggers via
                  window.location.assign(), window.location.href = ..., or location.reload().
                - If beforeEach does addInitScript(() => localStorage.removeItem('jwt'))
                  and your component does localStorage.setItem('jwt', token) followed
                  by window.location.assign('/'), the assign() reloads the page, the
                  init script fires again, and your JWT is wiped. The test then sees
                  jwt === null even though setItem clearly ran.
                - Symptom: assertion expects a localStorage value, receives null,
                  even though the component clearly sets it before redirect.
                - FIX in the component: use SOFT navigation instead of hard navigation:
                    window.history.pushState({}, '', '/');
                    window.dispatchEvent(new PopStateEvent('popstate'));
                  Soft navigation does NOT re-execute init scripts. localStorage is
                  preserved. Your App.js router listens to popstate and re-renders.
                - Do NOT mix: if your component does hard navigation, beforeEach must
                  use one-shot page.evaluate(...) instead of addInitScript(...).

                MOUNTING/ROUTING CHECK FIRST (before changing anything in the component):
                - If the failing test does `page.goto('/some-path')` and EVERY element
                  on that page is missing (label, heading, form, button), the new
                  component is NOT being mounted at that URL.
                - The dev server serves the same index.html for every URL. Visiting
                  /login renders App.js, NOT your Login.jsx — unless App.js is updated
                  to do that routing.
                - Check the previous attempt: did you include a modified src/App.js
                  that renders <Login /> when window.location.pathname === '/login'?
                  If NOT, that is the bug. Add an updated App.js to the response that
                  routes correctly. Do NOT keep tweaking LoginForm.jsx — the form is
                  fine, it just never renders.
                - Example minimal manual router (no react-router-dom needed):
                    import Login from './pages/Login';
                    export default function App() {
                      const [path, setPath] = React.useState(window.location.pathname);
                      React.useEffect(() => {
                        const h = () => setPath(window.location.pathname);
                        window.addEventListener('popstate', h);
                        return () => window.removeEventListener('popstate', h);
                      }, []);
                      if (path === '/login') return <Login />;
                      return <DefaultHome />;
                    }

                DEPENDENCY CHECK FIRST (do this before assuming the bug is in your code):
                - If the failing assertion is 'element not found' for ANY top-level element
                  (heading, form, button on the initial page), the page may not be rendering
                  AT ALL because of a 'Module not found' compile error. Look at every import
                  in your previous attempt and verify each package is in package.json from
                  REPO CONTEXT.
                - Common offenders missing from a default CRA repo: react-router-dom, axios,
                  redux, formik, styled-components.
                - If you find a missing dependency, rewrite the component WITHOUT it. Do NOT
                  keep tweaking auth logic / locators / loading states when the real problem
                  is that the page never loaded.

                SELF-CONTRADICTORY ASSERTION RULE — APPLY THIS BEFORE TOUCHING THE COMPONENT.

                MANDATORY MENTAL TEST: For the failing assertion, ask yourself:
                  ""After the natural sequence of steps that led here, would the element
                   targeted by this assertion still be in the DOM?""

                If the answer is NO, the assertion is the bug. Remove that single line.
                Do NOT modify the component to keep the element alive.

                Signal that an assertion is contradictory: the error says
                'element not found' / 'element(s) not found' / 'Expected: enabled,
                Timeout' for a locator that targets UI which an earlier step in the
                test has navigated/redirected/unmounted away from.

                CONCRETE EXAMPLES of contradictory assertions you must REMOVE:
                  * After successful login + redirect (to ANY path: '/', '/home',
                    '/protected', '/dashboard'), asserting that the Login form or
                    Login button is still visible/enabled. Login form unmounts when
                    you leave /login. There is no Login button there. Remove.
                  * After clicking Delete, asserting the deleted item is still visible.
                  * After form submit that shows a confirmation/replaces the form,
                    asserting the submit button is still enabled.
                  * After window.history.pushState + popstate that triggers a router
                    re-render, asserting the previous route's UI is still on screen.
                  * After window.location.assign(...) (a real page navigation),
                    asserting anything from the previous page state.

                FORBIDDEN 'fixes' for contradictory assertions:
                  * Forcing the form to remain mounted with key={counter}.
                  * Removing the post-login redirect so the Login form stays visible.
                  * Switching from soft navigation (pushState) to hard navigation
                    (location.assign) or vice versa to 'satisfy' the assertion.
                    Both will fail differently; the assertion is the problem.
                  * Keeping a 'success' message AND the original form simultaneously.

                ACTION: when you identify a contradictory assertion, delete THAT LINE
                from the test. Keep everything else. Do not delete the whole test.
                The test will still validate the loading state (which is the part
                that works), it just won't make a nonsensical post-redirect claim.

                CRITICAL CONSISTENCY RULES (test ↔ component):
                - The test and the production code MUST describe the SAME behavior.
                - If the test asserts a label like 'Logging in...' or a disabled state during submit,
                  the component MUST actually render that label AND set disabled={loading}.
                - If the test output says 'element not found' for a role/name, the component is
                  missing that label/role — FIX THE COMPONENT, do not just swap the locator.
                - If the test output says 'expected disabled, received enabled', the component is
                  not applying the disabled attribute — FIX THE COMPONENT.
                - Only change the test (instead of the component) when the test makes a clearly
                  wrong assumption — e.g. asserting a synchronous state that is inherently async,
                  or relying on Create-React-App default content that no longer exists.

                LOADING STATE TEST PATTERN (React/Playwright) — this is the EXACT ordering
                required for any assertion about a transient UI state. Misordering await calls
                is the #1 reason these tests fail when the component is actually correct:

                  // 1. Mock the API with a delay so the loading state is observable
                  await page.route('**/api/login', async route => {
                    await new Promise(r => setTimeout(r, 400));
                    await route.fulfill({ status: 200, body: JSON.stringify({ token: 'x' }) });
                  });

                  // 2. Fire the click but DO NOT await the fetch response yet
                  const clickPromise = page.click('button[type=\""submit\""]');

                  // 3. While the mock is still sleeping (400ms window), assert the loading UI
                  await expect(page.getByRole('button', { name: /logging in/i })).toBeDisabled();

                  // 4. NOW wait for the click action to settle
                  await clickPromise;

                  // 5. Assert the post-loading state
                  await expect(page.getByRole('button', { name: /^login$/i })).toBeEnabled();

                ANTI-PATTERN — DO NOT WRITE THIS. If your previous attempt used this pattern,
                that is exactly why the test failed. Rewrite it using the pattern above:

                  const [response] = await Promise.all([
                    page.waitForResponse('/api/login'),    // ← waits for response FIRST
                    page.click('button[type=\""submit\""]'),
                  ]);
                  await expect(page.getByRole('button', { name: /logging in/i })).toBeDisabled();
                  // By this point setLoading(false) has run; 'Logging in...' button no longer
                  // exists, assertion fails with 'element not found'. The component is fine.

                TESTS-REQUIRED RULE:
                - The 'tests' array MUST contain at least one test file.
                - Omitting tests is NOT a valid fix. Stale test files from prior attempts
                  remain on disk and will keep failing — you can only make a failing test
                  pass by rewriting it or by fixing the component it targets.
                - If the previous attempt included tests, your response must also include tests
                  (rewritten if needed).

                DIVERSITY RULE:
                - At least one production file OR one test file MUST differ meaningfully from
                  the previous attempt. Resubmitting identical code is forbidden.
                - If the failing assertion is the same as last time, you have NOT fixed it.
                  Look at the timing/ordering of awaits before changing locators.

                Return ONLY valid JSON (no markdown fences, no commentary):
                {
                  ""files"": [
                    { ""path"": """", ""content"": """" }
                  ],
                  ""tests"": [
                    { ""path"": """", ""content"": """" }
                  ]
                }
                ";

            var args = new KernelArguments
            {
                ["title"] = task.Title,
                ["description"] = task.Description,
                ["previous"] = prevJson,
                ["qaFeedback"] = qaFeedback,
                ["repo"] = repoContext
            };

            return (await _kernel.InvokePromptAsync(prompt, args)).ToString();
        }

        public void ValidateFiles(CodeResult result, string techStack)
        {
            var files = result.Files
                .Concat(result.Tests ?? Enumerable.Empty<GeneratedFile>());

            foreach (var file in files)
            {
                if (techStack.Contains(".NET") &&
                    !HasAllowedExtension(file.Path,
                        ".cs", ".csproj", ".json"))
                {
                    throw new Exception($"Invalid .NET file: {file.Path}");
                }

                if (techStack.Contains("React") &&
                    !HasAllowedExtension(file.Path,
                        ".jsx", ".tsx", ".js", ".ts",
                        ".css", ".scss", ".json"))
                {
                    throw new Exception($"Invalid React file: {file.Path}");
                }
            }
        }

        private bool HasAllowedExtension(string path, params string[] extensions)
        {
            return extensions.Any(path.EndsWith);
        }

        // Scans the imports of every generated JS/TS file and returns a list of
        // packages that are referenced but NOT declared in the target repo's
        // package.json. Catches "Module not found" errors before they cause an
        // unrelated Playwright timeout downstream.
        public List<string> FindUnresolvedImports(string repoPath, CodeResult result)
        {
            var packageJsonPath = Path.Combine(repoPath, "package.json");
            if (!File.Exists(packageJsonPath))
                return new List<string>();

            var pkg = Newtonsoft.Json.Linq.JObject.Parse(
                File.ReadAllText(packageJsonPath));

            var deps = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var section in new[]
                { "dependencies", "devDependencies", "peerDependencies" })
            {
                if (pkg[section] is Newtonsoft.Json.Linq.JObject obj)
                    foreach (var prop in obj.Properties())
                        deps.Add(prop.Name);
            }

            // node builtins — never need to be in package.json
            var nodeBuiltins = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
            {
                "fs", "path", "os", "crypto", "stream", "util", "events", "url",
                "http", "https", "buffer", "child_process", "querystring", "process",
                "assert", "zlib", "net", "tls", "dns", "tty", "readline", "cluster"
            };

            // packages that CRA / Vite resolve internally even if not in deps
            var implicitlyAvailable = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
            {
                "react/jsx-runtime", "react/jsx-dev-runtime"
            };

            var importRegex = new System.Text.RegularExpressions.Regex(
                @"(?:from|require\()\s*['""]([^'""]+)['""]",
                System.Text.RegularExpressions.RegexOptions.Compiled);

            var unresolved = new HashSet<string>();

            var allFiles = (result.Files ?? new List<GeneratedFile>())
                .Concat(result.Tests ?? new List<GeneratedFile>());

            foreach (var file in allFiles)
            {
                var ext = Path.GetExtension(file.Path)?.ToLowerInvariant();
                if (ext is not (".js" or ".jsx" or ".ts" or ".tsx" or ".mjs" or ".cjs"))
                    continue;

                foreach (System.Text.RegularExpressions.Match m in
                    importRegex.Matches(file.Content ?? ""))
                {
                    var spec = m.Groups[1].Value;

                    // relative or absolute paths — handled by the bundler, not packages
                    if (spec.StartsWith(".") || spec.StartsWith("/"))
                        continue;

                    // explicit submodule (e.g. 'lodash/debounce') — strip to root package
                    var pkgName = spec.StartsWith("@")
                        ? string.Join("/", spec.Split('/').Take(2))
                        : spec.Split('/')[0];

                    if (nodeBuiltins.Contains(pkgName)) continue;
                    if (implicitlyAvailable.Contains(spec)) continue;
                    if (deps.Contains(pkgName)) continue;

                    unresolved.Add($"{pkgName}  (imported in {file.Path})");
                }
            }

            return unresolved.OrderBy(s => s).ToList();
        }

        // Detects a project-language mismatch: AI generates .js/.jsx in a TypeScript
        // project (or .ts/.tsx in a JavaScript project). Both cause silent issues —
        // a .js file in a TS repo may not be picked up by tsconfig 'include', and
        // a .tsx in a CRA-JS repo won't compile without TS toolchain.
        public List<string> FindLanguageMismatches(string repoPath, CodeResult result)
        {
            if (result?.Files == null && result?.Tests == null) return new List<string>();

            var isTypeScriptProject = DetectTypeScriptProject(repoPath);

            var mismatches = new List<string>();
            var allFiles = (result.Files ?? new List<GeneratedFile>())
                .Concat(result.Tests ?? new List<GeneratedFile>());

            foreach (var f in allFiles)
            {
                var ext = Path.GetExtension(f.Path)?.ToLowerInvariant();

                if (isTypeScriptProject && (ext is ".js" or ".jsx"))
                {
                    mismatches.Add(
                        $"{f.Path}  (TypeScript project — use .ts or .tsx)");
                }
                else if (!isTypeScriptProject && (ext is ".ts" or ".tsx"))
                {
                    mismatches.Add(
                        $"{f.Path}  (JavaScript project — use .js or .jsx)");
                }
            }

            return mismatches;
        }

        private bool DetectTypeScriptProject(string repoPath)
        {
            // tsconfig.json is the strongest signal
            if (File.Exists(Path.Combine(repoPath, "tsconfig.json")))
                return true;

            // typescript listed as a dependency
            var packageJsonPath = Path.Combine(repoPath, "package.json");
            if (File.Exists(packageJsonPath))
            {
                try
                {
                    var pkg = Newtonsoft.Json.Linq.JObject.Parse(
                        File.ReadAllText(packageJsonPath));
                    foreach (var section in new[]
                        { "dependencies", "devDependencies", "peerDependencies" })
                    {
                        if (pkg[section] is Newtonsoft.Json.Linq.JObject obj
                            && obj["typescript"] != null)
                            return true;
                    }
                }
                catch { /* malformed package.json — ignore */ }
            }

            return false;
        }

        public bool TryParseCodeResult(
            string json,
            out CodeResult result,
            out string error)
        {
            result = null;
            error = null;

            try
            {
                // remove markdown fences if AI adds them
                json = json
                    .Replace("```json", "")
                    .Replace("```", "")
                    .Trim();

                result =
                    JsonConvert.DeserializeObject<CodeResult>(json);

                if (result == null)
                {
                    error = "Deserialized object is null";
                    return false;
                }

                return true;
            }
            catch (Exception ex)
            {
                error = ex.Message;
                return false;
            }
        }
    }
}