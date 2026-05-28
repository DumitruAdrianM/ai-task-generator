using Newtonsoft.Json;
using System.Text;

namespace AiTaskGenerator
{
    public class NotionService
    {
        private readonly HttpClient _httpClient;
        private readonly string _databaseId;
        private readonly string _token;

        public NotionService(HttpClient httpClient, IConfiguration config)
        {
            _httpClient = httpClient;
            _token = config["Notion:Token"];
            _databaseId = config["Notion:DatabaseId"];
        }

        public async Task CreateTask(TaskItem task)
        {
            var properties = new Dictionary<string, object>
            {
                ["Title"] = new
                {
                    title = new[]
                    {
                        new { text = new { content = task.Title ?? "" } }
                    }
                },
                ["Type"] = new
                {
                    select = new { name = task.Type ?? "Backend" }
                },
                ["Description"] = new
                {
                    rich_text = new[]
                    {
                        new { text = new { content = task.Description ?? "" } }
                    }
                },
                ["Acceptance Criteria"] = new
                {
                    rich_text = new[]
                    {
                        new { text = new { content = task.AcceptanceCriteria ?? "" } }
                    }
                },
                ["TechStack"] = new
                {
                    select = new
                    {
                        name = task.TechStack ?? ".NET"
                    }
                },
                ["RepoPath"] = new
                {
                    rich_text = new[]
                {
                    new { text = new { content = task.RepoPath ?? "" } }
                }
                }
            };

            var body = new
            {
                parent = new { database_id = _databaseId },
                properties = properties
            };

            var request = new HttpRequestMessage(HttpMethod.Post, "https://api.notion.com/v1/pages");

            request.Headers.Add("Authorization", $"Bearer {_token}");
            request.Headers.Add("Notion-Version", "2022-06-28");

            var json = JsonConvert.SerializeObject(body);

            request.Content = new StringContent(json, Encoding.UTF8, "application/json");

            var response = await _httpClient.SendAsync(request);
            var responseContent = await response.Content.ReadAsStringAsync();

            if (!response.IsSuccessStatusCode)
            {
                throw new Exception($"Notion API error: {response.StatusCode} - {responseContent}");
            }
        }

        public async Task<TaskItem> GetTask(string pageId)
        {
            var request = new HttpRequestMessage(
                HttpMethod.Get,
                $"https://api.notion.com/v1/pages/{pageId}"
            );

            request.Headers.Add("Authorization", $"Bearer {_token}");
            request.Headers.Add("Notion-Version", "2022-06-28");

            var response = await _httpClient.SendAsync(request);
            var json = await response.Content.ReadAsStringAsync();

            if (!response.IsSuccessStatusCode)
                throw new Exception($"Notion error: {json}");

            dynamic data = JsonConvert.DeserializeObject(json);

            return new TaskItem
            {
                Id = pageId,
                Title = GetTitle(data.properties.Title),
                Type = data.properties.Type?.select?.name,
                Description = GetRichText(data.properties.Description),
                AcceptanceCriteria = GetRichText(data.properties["Acceptance Criteria"]),
                TechStack = data.properties.TechStack?.select?.name,
                RepoPath = GetRichText(data.properties.RepoPath)
            };
        }

        public async Task UpdateTask(TaskItem task)
        {
            var url = $"https://api.notion.com/v1/pages/{task.Id}";

            var body = new
            {
                properties = new
                {
                    Status = new
                    {
                        select = new { name = task.Status }
                    },
                    BranchName = new
                    {
                        rich_text = new[]
                        {
                        new
                            {
                                text = new { content = task.BranchName ?? "" }
                            }
                        }
                    },
                    PullRequestUrl = new
                    {
                        rich_text = new[]
                        {
                        new
                            {
                                text = new { content = task.PullRequestUrl ?? "" }
                            }
                        }
                    }
                }
            };

            var request = new HttpRequestMessage(HttpMethod.Patch, url);
            request.Headers.Add("Authorization", $"Bearer {_token}");
            request.Headers.Add("Notion-Version", "2022-06-28");

            request.Content = new StringContent(
                JsonConvert.SerializeObject(body),
                Encoding.UTF8,
                "application/json"
            );

            var response = await _httpClient.SendAsync(request);

            if (!response.IsSuccessStatusCode)
            {
                var error = await response.Content.ReadAsStringAsync();
                throw new Exception($"Notion update failed: {error}");
            }
        }

        public async Task<List<TaskItem>> GetTasksByStatus(string status)
        {
            var url = $"https://api.notion.com/v1/databases/{_databaseId}/query";

            var body = new
            {
                filter = new
                {
                    property = "Status",
                    select = new
                    {
                        equals = status
                    }
                }
            };

            var request = new HttpRequestMessage(HttpMethod.Post, url);
            request.Headers.Add("Authorization", $"Bearer {_token}");
            request.Headers.Add("Notion-Version", "2022-06-28");

            request.Content = new StringContent(
                JsonConvert.SerializeObject(body),
                Encoding.UTF8,
                "application/json"
            );

            var response = await _httpClient.SendAsync(request);
            var json = await response.Content.ReadAsStringAsync();

            if (!response.IsSuccessStatusCode)
                throw new Exception($"Notion query failed: {json}");

            dynamic data = JsonConvert.DeserializeObject(json);

            var results = new List<TaskItem>();

            if (data.results == null)
                return results;

            foreach (var page in data.results)
            {
                var task = new TaskItem
                {
                    Id = page.id,

                    Title = GetPlainText(page.properties?.Title?.title),

                    Status = page.properties?.Status?.select?.name,

                    BranchName = GetPlainText(page.properties?.BranchName?.rich_text),

                    TechStack = page.properties?.TechStack?.select?.name
                };

                results.Add(task);
            }

            return results;
        }

        string GetRichText(dynamic prop)
        {
            return prop?.rich_text?[0]?.text?.content ?? "";
        }

        string GetTitle(dynamic prop)
        {
            return prop?.title?[0]?.text?.content ?? "";
        }

        private string GetPlainText(dynamic arr)
        {
            if (arr == null)
                return null;

            try
            {
                if (arr.Count > 0)
                    return arr[0].plain_text;
            }
            catch { }

            return null;
        }
    }
}