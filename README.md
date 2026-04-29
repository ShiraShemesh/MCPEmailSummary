# Gmail AI Assistant

Secure Gmail assistant that analyzes emails using Google Gemini AI.

## What It Does

- Reads your last 5 emails from Gmail
- Summarizes them with AI
- Suggests replies for urgent emails
- Creates draft emails automatically

## Requirements

You MUST have these 3 things or the app will NOT work:

1. **Gmail Client ID** - Get from [Google Cloud Console](https://console.cloud.google.com)
2. **Gmail Client Secret** - Get from [Google Cloud Console](https://console.cloud.google.com)
3. **Gemini API Key** - Get from [Google AI Studio](https://aistudio.google.com/app/apikey)

Without all 3, you will get an error message and cannot use the app.

## Setup Instructions

### 1. Get Your API Keys

Go to Google Cloud Console:
- Create a new project
- Enable Gmail API
- Create OAuth 2.0 credentials (Desktop app)
- Copy client_id and client_secret

Go to Google AI Studio:
- Create API key
- Copy it

### 2. Install .NET 9.0

Download from: https://dotnet.microsoft.com/download

### 3. Download This Project

```bash
git clone https://github.com/yourusername/SmartRecipeBox.git
cd SmartRecipeBox
```

### 4. Create Configuration File

In the project folder, create a file named: `appsettings.Development.json`

Write this inside (replace YOUR_XXX with your actual keys):

```json
{
  "Logging": {
    "LogLevel": {
      "Default": "Information",
      "Microsoft.AspNetCore": "Warning"
    }
  },
  "Gmail": {
    "client_id": "YOUR_CLIENT_ID_HERE",
    "client_secret": "YOUR_CLIENT_SECRET_HERE"
  },
  "GeminiApiKey": "YOUR_GEMINI_API_KEY_HERE"
}
```

### 5. Run The App

Open Command Prompt in the project folder and type:

```bash
dotnet run
```

Open your browser: https://localhost:5001

Click "Scan and Summarize Emails"

Sign in with your Google account

## Security

- Your email addresses are NEVER sent to AI
- Only sender names and email content are used
- Your keys stay on your computer (not uploaded to Git)
- Google handles all authentication securely

## Project Files

- `Controllers/MailController.cs` - Receives requests from browser
- `Services/McpExplainOrchestrator.cs` - Main logic (Gmail + AI)
- `wwwroot/index.html` - The website you see
- `appsettings.Development.json` - Your secret keys (not in Git)

## Error Messages

**"Missing configuration keys"** → You didn't create appsettings.Development.json correctly

**"Failed to authorize"** → Your Gmail API is not enabled in Google Cloud

**"Empty mailbox"** → Send a test email and try again

## Troubleshooting

If it doesn't work, try:

```bash
dotnet clean
dotnet restore
dotnet run
```

## License

MIT License
