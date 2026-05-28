using AiTaskGenerator;
using Newtonsoft.Json;

var builder = WebApplication.CreateBuilder(args);

// add services
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen();

// register services
builder.Services.AddSingleton<AiService>();
builder.Services.AddHttpClient<NotionService>(); 
builder.Services.AddSingleton<CodeGenerationService>();
builder.Services.AddSingleton<GitService>();
builder.Services.AddSingleton<RepoReaderService>();
builder.Services.AddSingleton<GitHubService>();
builder.Services.AddSingleton<QaService>();
//builder.Services.AddSingleton<QaGenerationService>();

var app = builder.Build();

// swagger
if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI();
}

app.UseHttpsRedirection();

app.MapPost("/generate-tasks", async (string input, AiService aiService, NotionService notionService) =>
{
    // genereaza taskuri cu AI
    var json = await aiService.GenerateTasks(input);

    // deserializeaza
    var taskResponse = Newtonsoft.Json.JsonConvert.DeserializeObject<TaskResponse>(json);

    // trimite in Notion
    foreach (var task in taskResponse.Tasks)
    {
        await notionService.CreateTask(task);
    }

    return Results.Ok(taskResponse);
})
.WithName("GenerateTasks");

//app.MapPost("/generate-code", async (
//    string taskId,
//    CodeGenerationService codeService,
//    RepoReaderService repoReader,
//    NotionService notionService,
//    FileWriterService fileWriter,
//    GitService gitService,
//    GitHubService gitHubService,
//    IConfiguration config) =>
//{
//    var task = await notionService.GetTask(taskId);

//    var repoContext = repoReader.ReadRelevantFiles(task.RepoPath, task.TechStack);

//    var json = await codeService.GenerateCode(task, repoContext);
//    var codeResult = JsonConvert.DeserializeObject<CodeResult>(json);

//    codeService.ValidateFiles(codeResult, task.TechStack);

//    var branchName = $"{task.Type}-{Guid.NewGuid()}";

//    gitService.CreateBranch(task.Type, branchName);
//    fileWriter.WriteFiles(config[$"Git:{task.Type}RepoPath"], codeResult.Files);

//    //  commit + push
//    gitService.Commit(task.Type, $"AI draft: {task.Title}");
//    gitService.Push(task.Type, branchName);

//    // CREATE PR
//    var prUrl = await gitHubService.CreatePullRequest(
//        task.Type,   // Backend / Frontend
//        branchName,
//        $"[AI] {task.Title}",
//        task.Description
//    );

//    //  update task
//    task.Status = "InReview";
//    task.BranchName = branchName;
//    task.PullRequestUrl = prUrl;
//    task.Id = taskId;

//    await notionService.UpdateTask(task);

//    return Results.Ok(new
//    {
//        branch = branchName,
//        pr = prUrl
//    });
//}).WithName("GenerateCode");

//app.MapPost("/generate-code", async (
//    string taskId,
//    CodeGenerationService codeService,
//    RepoReaderService repoReader,
//    NotionService notionService,
//    FileWriterService fileWriter,
//    GitService gitService,
//    GitHubService gitHubService,
//    QaService qaService,
//    IConfiguration config) =>
//{
//    var task = await notionService.GetTask(taskId);

//    var repoContext = repoReader.ReadRelevantFiles(
//        task.RepoPath,
//        task.TechStack);

//    var json = await codeService.GenerateCode(task, repoContext);

//    var codeResult =
//        JsonConvert.DeserializeObject<CodeResult>(json);

//    codeService.ValidateFiles(codeResult, task.TechStack);

//    var repoPath = config[$"Git:{task.Type}RepoPath"];

//    var branchName = $"{task.Type}-{Guid.NewGuid()}";

//    // branch
//    gitService.CreateBranch(task.Type, branchName);

//    // code files
//    fileWriter.WriteFiles(repoPath, codeResult.Files);

//    // generated tests
//    fileWriter.WriteFiles(repoPath, codeResult.Tests);

//    // commit code + tests
//    gitService.Commit(task.Type, $"AI draft: {task.Title}");

//    // push
//    gitService.Push(task.Type, branchName);

//    // RUN QA
//    var qaResult = qaService.Run(task);

//    // create PR
//    var prUrl = await gitHubService.CreatePullRequest(
//        task.Type,
//        branchName,
//        $"[AI] {task.Title}",
//        task.Description
//    );

//    // attach QA evidence
//    await gitHubService.CommentOnPullRequest(
//        task.Type,
//        prUrl,
//        qaResult
//    );

//    task.Status = qaResult.Success
//        ? "QaPassed"
//        : "QaFailed";

//    task.BranchName = branchName;
//    task.PullRequestUrl = prUrl;

//    await notionService.UpdateTask(task);

//    return Results.Ok(new
//    {
//        branch = branchName,
//        pr = prUrl,
//        qa = qaResult.Success
//    });
//}).WithName("GenerateCode");

app.MapPost("/generate-code", async (
    string taskId,
    CodeGenerationService codeService,
    RepoReaderService repoReader,
    NotionService notionService,
    FileWriterService fileWriter,
    GitService gitService,
    GitHubService gitHubService,
    QaService qaService,
    IConfiguration config) =>
{
    var task = await notionService.GetTask(taskId);

    var repoPath = config[$"Git:{task.Type}RepoPath"];

    var branchName = $"{task.Type}-{Guid.NewGuid()}";

    gitService.CreateBranch(task.Type, branchName);

    QaResult qaResult = null;
    CodeResult codeResult = null;
    var previouslyWrittenTests = new List<string>();   // paths written in the prior iteration

    const int maxAttempts = 5;

    for (int attempt = 1; attempt <= maxAttempts; attempt++)
    {
        // re-read context every iteration so the AI sees its own previous writes
        var repoContext = repoReader.ReadRelevantFiles(
            task.RepoPath,
            task.TechStack);

        string json;

        try
        {
            if (attempt == 1)
            {
                json = await codeService.GenerateCode(
                    task,
                    repoContext);
            }
            else
            {
                json = await codeService.RegenerateCode(
                    task,
                    repoContext,
                    qaService.BuildQaFeedback(qaResult),
                    codeResult);
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"\n========== ATTEMPT {attempt} — AI CALL FAILED ==========");
            Console.WriteLine($"{ex.GetType().Name}: {ex.Message}");

            qaResult = new QaResult
            {
                Success = false,
                Output = "",
                Error = $"AI call failed: {ex.GetType().Name}: {ex.Message}"
            };

            continue;
        }

        if (!codeService.TryParseCodeResult(
        json,
        out codeResult,
        out var parseError))
        {
            Console.WriteLine($"\n========== ATTEMPT {attempt} — JSON PARSE FAILED ==========");
            Console.WriteLine($"Error: {parseError}");
            Console.WriteLine($"Raw: {json}");

            qaResult = new QaResult
            {
                Success = false,
                Error = $"Invalid JSON returned by AI: {parseError}",
                Output = json
            };

            continue;
        }

        Console.WriteLine($"\n========== ATTEMPT {attempt} — GENERATED FILES ==========");
        foreach (var f in codeResult.Files ?? new List<GeneratedFile>())
        {
            Console.WriteLine($"\n--- FILE: {f.Path} ---");
            Console.WriteLine(f.Content);
        }
        foreach (var t in codeResult.Tests ?? new List<GeneratedFile>())
        {
            Console.WriteLine($"\n--- TEST: {t.Path} ---");
            Console.WriteLine(t.Content);
        }
        Console.WriteLine($"========== END ATTEMPT {attempt} ==========\n");

        try
        {
            codeService.ValidateFiles(
                codeResult,
                task.TechStack);
        }
        catch (Exception ex)
        {
            qaResult = new QaResult
            {
                Success = false,
                Error = ex.Message,
                Output = json
            };

            continue;
        }

        // delete stale tests from the previous iteration so AI cannot "fix" failures
        // by simply omitting tests — the old test files would otherwise persist on disk
        // and keep failing
        foreach (var oldTestPath in previouslyWrittenTests)
        {
            var fullOld = Path.Combine(repoPath, oldTestPath);
            try { if (File.Exists(fullOld)) File.Delete(fullOld); }
            catch (Exception ex)
            {
                Console.WriteLine($"Warning: could not delete stale test {fullOld}: {ex.Message}");
            }
        }

        // Detect language mismatch: .js/.jsx in a TypeScript repo (or vice versa).
        // tsconfig 'include' patterns may skip the wrong-language file, leaving the
        // page silently un-updated. Catch before write so the orchestrator can hand
        // back a precise error message.
        var languageMismatches = codeService.FindLanguageMismatches(repoPath, codeResult);
        if (languageMismatches.Count > 0)
        {
            Console.WriteLine($"\n========== ATTEMPT {attempt} — LANGUAGE MISMATCH ==========");
            foreach (var m in languageMismatches) Console.WriteLine("  " + m);

            qaResult = new QaResult
            {
                Success = false,
                Output = "",
                Error =
                    "LANGUAGE MISMATCH — generated files do not match the project's " +
                    "language. The repo uses TypeScript (or JavaScript) and you " +
                    "generated files in the other language.\n\n" +
                    "Mismatched files:\n" +
                    string.Join("\n", languageMismatches.Select(m => "- " + m)) +
                    "\n\nFix: regenerate using the correct extensions. In a TypeScript " +
                    "project, use .ts for non-JSX files and .tsx for components with JSX, " +
                    "and include proper type annotations on props, state, and signatures."
            };

            continue;
        }

        // Detect missing-package imports BEFORE writing/running anything. A 'Module
        // not found' compile error makes the dev server serve a blank page, which
        // Playwright reports as 'element not found' — a misleading signal that
        // sends AI debugging the wrong thing for 5 iterations.
        var unresolved = codeService.FindUnresolvedImports(repoPath, codeResult);
        if (unresolved.Count > 0)
        {
            Console.WriteLine($"\n========== ATTEMPT {attempt} — UNRESOLVED IMPORTS ==========");
            foreach (var u in unresolved) Console.WriteLine("  " + u);

            qaResult = new QaResult
            {
                Success = false,
                Output = "",
                Error =
                    "DEPENDENCY ERROR — your code imports packages that are NOT in " +
                    "package.json. The build will fail and the page will not render; " +
                    "Playwright will report 'element not found' for the FIRST heading " +
                    "even though your component code is unrelated.\n\n" +
                    "Missing packages:\n" +
                    string.Join("\n", unresolved.Select(u => "- " + u)) +
                    "\n\nFIX OPTIONS:\n" +
                    "1. Implement the feature without these packages (e.g. window.history\n" +
                    "   + conditional rendering instead of react-router-dom; plain fetch\n" +
                    "   instead of axios; useState/useReducer instead of redux).\n" +
                    "2. If absolutely necessary, return only a single file ERROR.md\n" +
                    "   explaining which dependency must be installed."
            };

            continue;
        }

        fileWriter.WriteFiles(repoPath, codeResult.Files);
        fileWriter.WriteFiles(repoPath, codeResult.Tests);

        previouslyWrittenTests = codeResult.Tests?.Select(t => t.Path).ToList()
                                 ?? new List<string>();

        qaResult = await qaService.Run(task, codeResult.Tests);

        if (qaResult.Success)
            break;
    }

    if (!qaResult.Success)
    {
        task.Status = "QaFailed";
        await notionService.UpdateTask(task);

        return Results.BadRequest(new
        {
            status = "QA failed after max retries",
            output = qaResult.Output,
            error = qaResult.Error
        });
    }

    gitService.Commit(task.Type, $"AI draft: {task.Title}");
    gitService.Push(task.Type, branchName);

    var prUrl = await gitHubService.CreatePullRequest(
        task.Type,
        branchName,
        $"[AI] {task.Title}",
        task.Description);

    await gitHubService.CommentOnPullRequest(
        task.Type,
        prUrl,
        qaResult);

    task.Status = "QaPassed";
    task.BranchName = branchName;
    task.PullRequestUrl = prUrl;

    await notionService.UpdateTask(task);

    return Results.Ok(new
    {
        branch = branchName,
        pr = prUrl,
        attempts = maxAttempts,
        qa = true
    });
});

//app.MapGet("/tasks/pending-review", async (NotionService notionService) =>
//{
//    var tasks = await notionService.GetTasksByStatus("InReview");
//    return Results.Ok(tasks);
//});

//app.MapPost("/run-qa", async (
//    string taskId,
//    NotionService notionService,
//    QaGenerationService qaGeneration,
//    QaService qaService,
//    FileWriterService fileWriter,
//    IConfiguration config) =>
//{
//    var task = await notionService.GetTask(taskId);

//    if (task.TechStack.Contains("React"))
//    {
//        var tests = await qaGeneration.GenerateTests(task);

//        var repo = config["Git:FrontendRepoPath"];

//        File.WriteAllText(
//            Path.Combine(repo, "tests", $"{task.Id}.spec.ts"),
//            tests
//        );
//    }

//    var result = qaService.Run(task);

//    task.Status = result.Success
//        ? "QaPassed"
//        : "QaFailed";

//    await notionService.UpdateTask(task);

//    return Results.Ok(result);
//}).WithName("RunQa");

app.Run();