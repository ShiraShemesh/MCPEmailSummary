using Google.Apis.Auth.OAuth2;
using Google.Apis.Gmail.v1;
using Google.Apis.Gmail.v1.Data;
using Google.Apis.Services;
using Google.Apis.Util.Store;
using Microsoft.Extensions.Configuration;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace SmartRecipeBox.Services;

public sealed class McpExplainOrchestrator
{
    private readonly IConfiguration _configuration;
    private static readonly string[] Scopes = { GmailService.Scope.GmailReadonly, GmailService.Scope.GmailCompose };

    public McpExplainOrchestrator(IConfiguration configuration) => _configuration = configuration;

    public async Task<AiDecision?> ProcessEmailsAndSuggestDraftsAsync(CancellationToken ct)
    {
        var clientId = _configuration["Gmail:client_id"];
        var clientSecret = _configuration["Gmail:client_secret"];
        var apiKey = _configuration["GeminiApiKey"];

        // בדיקה משופרת עם הודעות ברורות
        var missingKeys = new List<string>();

        if (string.IsNullOrEmpty(clientId)) missingKeys.Add("Gmail:client_id");
        if (string.IsNullOrEmpty(clientSecret)) missingKeys.Add("Gmail:client_secret");
        if (string.IsNullOrEmpty(apiKey)) missingKeys.Add("GeminiApiKey");

        if (missingKeys.Count > 0)
        {
            var keysList = string.Join(", ", missingKeys);
            Console.WriteLine($"[ERROR] Missing configuration keys: {keysList}");
            return new AiDecision
            {
                Summary = $"⚠️ שגיאת הגדרה: חסרים המפתחות: {keysList}. בדוק את appsettings.Development.json",
                NeedsReply = false
            };
        }


        try
        {
            var credential = await GoogleWebAuthorizationBroker.AuthorizeAsync(
                new ClientSecrets { ClientId = clientId, ClientSecret = clientSecret },
                Scopes,
                "user",
                ct,
                new FileDataStore(Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "SecureTokenStore"), true),
                new LocalServerCodeReceiver()
            );

            var gmail = new GmailService(new BaseClientService.Initializer { HttpClientInitializer = credential });

            var listResponse = await gmail.Users.Messages.List("me").ExecuteAsync(ct);
            if (listResponse.Messages == null || listResponse.Messages.Count == 0)
                return new AiDecision { Summary = "תיבת הדואר ריקה.", NeedsReply = false };

            var tasks = listResponse.Messages.Take(5).Select(m => gmail.Users.Messages.Get("me", m.Id).ExecuteAsync(ct));
            var fullMessages = await Task.WhenAll(tasks);

            var emailsList = new List<EmailData>();
            var context = new StringBuilder();

            foreach (var msg in fullMessages)
            {
                var fromRaw = GetHeader(msg, "From");

                var senderName = Regex.Replace(fromRaw, @"<.*?>", "").Replace("\"", "").Trim();
                if (string.IsNullOrEmpty(senderName)) senderName = "שולח לא ידוע";

                var subject = GetHeader(msg, "Subject");
                var snippet = msg.Snippet ?? "[תוכן ריק]";

                emailsList.Add(new EmailData
                {
                    MessageId = msg.Id,
                    SenderName = senderName,
                    SenderEmail = fromRaw,
                    Subject = subject,
                    Snippet = snippet,
                    ThreadId = msg.ThreadId
                });

                context.AppendLine($"--- מייל {emailsList.Count} ---");
                context.AppendLine($"משלח: {senderName}");
                context.AppendLine($"נושא: {subject}");
                context.AppendLine($"תוכן: {snippet}");
                context.AppendLine();
            }

            var aiResult = await CallGeminiAsync(apiKey, context.ToString(), ct);

            if (aiResult != null && aiResult.NeedsReply && aiResult.DraftReplies != null && aiResult.DraftReplies.Count > 0)
            {
                foreach (var draftReply in aiResult.DraftReplies)
                {
                    var emailData = emailsList.FirstOrDefault(e => e.SenderName == draftReply.TargetSenderName);
                    if (emailData != null)
                    {
                        await CreateSuggestedDraftAsync(gmail, emailData, draftReply.Reply ?? "");
                    }
                }
            }

            return aiResult;
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[ERROR] ProcessEmails failed: {ex.Message}");
            return new AiDecision
            {
                Summary = $"⚠️ שגיאה: {ex.Message}. אם זו בעיית הגדרות, בדוק את appsettings.Development.json",
                NeedsReply = false
            };

        }
    }

    private async Task<AiDecision?> CallGeminiAsync(string apiKey, string emailContext, CancellationToken ct)
    {
        using var client = new HttpClient();
        client.Timeout = TimeSpan.FromSeconds(60);

        var fullPrompt = @"You are a Gmail assistant. Analyze these emails and return ONLY valid JSON, no extra text, no markdown tags.

Return a JSON object with these exact fields:
- ""Summary"": A well-formatted, readable summary in Hebrew. Format like:
  ""(שירה) שלחה מייל בנושא: 'פרויקט חדש' - עם בקשה לדעתך על הקונספט.
  (ישראל) שלח מייל בנושא: 'דחיפות' - כדי להודיע על עיכוב.""
  Use sender names in parentheses, then action, subject in quotes. Use bullet points if multiple emails.
  
- ""NeedsReply"": boolean true if any email needs urgent reply
- ""DraftReplies"": array of objects with:
    - ""TargetSenderName"": The sender's name (not email!) you're replying to
    - ""Reply"": suggested reply text in Hebrew
  Return empty array [] if NeedsReply is false

EMAILS:
" + emailContext;

        var payload = new
        {
            contents = new object[] { new { parts = new object[] { new { text = fullPrompt } } } }
        };

        var url = $"https://generativelanguage.googleapis.com/v1beta/models/gemini-2.5-flash-lite:generateContent?key={apiKey}";

        try
        {
            var response = await client.PostAsJsonAsync(url, payload, ct);

            if (!response.IsSuccessStatusCode)
            {
                return new AiDecision { Summary = $"שגיאת API: {response.StatusCode}", NeedsReply = false };
            }

            var rawResponse = await response.Content.ReadFromJsonAsync<JsonElement>(ct);
            var text = rawResponse.GetProperty("candidates")[0].GetProperty("content").GetProperty("parts")[0].GetProperty("text").GetString();

            return SafeParseAiJson(text);
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[ERROR] Gemini call failed: {ex.Message}");
            return new AiDecision { Summary = "שגיאה בגישה ל-AI.", NeedsReply = false };
        }
    }

    private AiDecision? SafeParseAiJson(string? rawText)
    {
        if (string.IsNullOrWhiteSpace(rawText))
            return new AiDecision { Summary = "ה-AI החזיר תשובה ריקה." };

        try
        {
            var cleanedText = rawText.Trim();
            if (cleanedText.StartsWith("```json")) cleanedText = cleanedText.Substring(7);
            if (cleanedText.StartsWith("```")) cleanedText = cleanedText.Substring(3);
            if (cleanedText.EndsWith("```")) cleanedText = cleanedText.Substring(0, cleanedText.Length - 3);

            var match = Regex.Match(cleanedText, @"\{[\s\S]*\}");
            if (match.Success)
            {
                return JsonSerializer.Deserialize<AiDecision>(match.Value, new JsonSerializerOptions
                {
                    PropertyNameCaseInsensitive = true,
                    AllowTrailingCommas = true,
                    ReadCommentHandling = JsonCommentHandling.Skip
                });
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[WARN] JSON parse error: {ex.Message}");
        }

        return new AiDecision
        {
            Summary = rawText.Length > 200 ? rawText.Substring(0, 200) + "..." : rawText,
            NeedsReply = false,
        };
    }

    private async Task CreateSuggestedDraftAsync(GmailService service, EmailData emailData, string draftContent)
    {
        var subject = emailData.Subject;

        if (!subject.StartsWith("Re:", StringComparison.OrdinalIgnoreCase))
        {
            subject = "Re: " + subject;
        }

        string rawMessage = $"To: {emailData.SenderEmail}\r\nSubject: {subject}\r\nContent-Type: text/plain; charset=utf-8\r\n\r\n{draftContent}";

        var draft = new Draft
        {
            Message = new Message
            {
                Raw = Convert.ToBase64String(Encoding.UTF8.GetBytes(rawMessage)).Replace('+', '-').Replace('/', '_').Replace("=", ""),
                ThreadId = emailData.ThreadId
            }
        };
        await service.Users.Drafts.Create(draft, "me").ExecuteAsync();
    }

    private string GetHeader(Message msg, string name) =>
        msg.Payload.Headers.FirstOrDefault(h => h.Name.Equals(name, StringComparison.OrdinalIgnoreCase))?.Value ?? "";
}

public class EmailData
{
    public string MessageId { get; set; } = "";
    public string SenderName { get; set; } = "";
    public string SenderEmail { get; set; } = "";
    public string Subject { get; set; } = "";
    public string Snippet { get; set; } = "";
    public string? ThreadId { get; set; }
}
public sealed class AiDecision
{
    public string Summary { get; set; } = "";
    public bool NeedsReply { get; set; }
    public List<DraftReply> DraftReplies { get; set; } = new();
}

public class DraftReply
{
    public string TargetSenderName { get; set; } = "";
    public string? Reply { get; set; }
}