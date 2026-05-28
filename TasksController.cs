//using Microsoft.AspNetCore.Mvc;
//using Newtonsoft.Json;

//namespace AiTaskGenerator
//{
//    [ApiController]
//    [Route("api/tasks")]
//    public class TasksController : ControllerBase
//    {
//        private readonly AiService _aiService;
//        private readonly NotionService _notionService;

//        public TasksController(AiService aiService, NotionService notionService)
//        {
//            _aiService = aiService;
//            _notionService = notionService;
//        }

//        [HttpPost("generate")]
//        public async Task<IActionResult> Generate([FromBody] string input)
//        {
//            var json = await _aiService.GenerateTasks(input);

//            var tasks = JsonConvert.DeserializeObject<TaskResponse>(json);

//            foreach (var task in tasks.Tasks)
//            {
//                await _notionService.CreateTask(task);
//            }

//            return Ok(tasks);
//        }
//    }
//}
