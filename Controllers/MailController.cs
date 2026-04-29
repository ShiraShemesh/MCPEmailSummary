using Microsoft.AspNetCore.Mvc;
using SmartRecipeBox.Services;

namespace SmartRecipeBox.Controllers
{
    [ApiController]
    [Route("api/[controller]")]
    public class MailController : ControllerBase
    {
        private readonly McpExplainOrchestrator _orchestrator;

        public MailController(McpExplainOrchestrator orchestrator)
        {
            _orchestrator = orchestrator;
        }

        [HttpGet("summarize")]
        public async Task<IActionResult> GetSummary(CancellationToken ct)
        {
            try
            {
                var result = await _orchestrator.ProcessEmailsAndSuggestDraftsAsync(ct);

                if (result == null)
                    return BadRequest(new { error = "No result from AI" });

                return Ok(result);
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[ERROR] GetSummary: {ex.Message}");
                return StatusCode(500, new
                {
                    error = "שגיאה בטעינת המערכת",
                    details = ex.Message,
                    hint = "בדוק את appsettings.Development.json"
                });
            }
        }

        [HttpPost("logout")]
        public IActionResult Logout()
        {
            try
            {
                var tokenPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "SecureTokenStore");
                if (Directory.Exists(tokenPath))
                {
                    Directory.Delete(tokenPath, true);
                }
                return Ok(new { message = "Logged out successfully" });
            }
            catch (Exception ex)
            {
                return StatusCode(500, new { error = ex.Message });
            }
        }
    }
}