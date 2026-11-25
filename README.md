# BlotterSync

An offline-capable barangay incident workspace built on ASP.NET Core 8. Desk Chat provides conversations with an optional incident attachment, source references and tool activity. Officers can also prepare and review incident briefs and intake drafts. The original dashboard and community kiosk remain available.

## Run the development demo

Requires the .NET 8 SDK. From the repository root:

```sh
dotnet restore BlotterSync/BlotterSync.csproj
dotnet run --project BlotterSync/BlotterSync.csproj --launch-profile http
```

Open [the officer workspace](http://localhost:5180/workspace.html) and choose **Explore the demo workspace**. Development demo mode seeds eight synthetic incidents in `BlotterSync/App_Data/blottersync-demo.db`. The demo login endpoint is unavailable outside Development or when `Demo:Enabled` is false. The development JWT signing key is generated per process unless configured, so sign in again after restarting.

The runtime frontend is in `BlotterSync/wwwroot`; repository-root HTML files are retained upstream snapshots, not the runtime entrypoints. The application serves the workspace at `/`. `/index.html` retains officer login/registration, `/dashboard.html` retains the legacy dashboard, and `/kiosk.html` retains the community kiosk. The `.slnx` came from the upstream repository; the project commands above work directly with .NET 8.

## Desk Chat and model setup

Open **Desk Chat** in the workspace sidebar. Choose an incident to give the conversation a source, then ask about the reported details or what to verify. Open **Tool activity** under a reply to see what the assistant read. **Recent chats** reopens saved conversations. Start a new chat to change its attached incident.

The same harness supports Desk Chat, incident briefs and intake drafts.

| Provider | Server setup |
| --- | --- |
| Local rules | Set `AI__Provider=local`, or run with the default configuration and no OpenAI key. Chat can look up record facts, list categories and offer intake checklists. Drafts use verbatim extraction. |
| OpenAI | Set `AI__Provider=openai` and `OPENAI_API_KEY` or the `AI:ApiKey` development user secret. Set `AI__Model` to override `gpt-5.4-mini`. |
| Ollama | Set `AI__Provider=ollama` and point the server at an existing Ollama instance with a tool-capable model that supports structured outputs. No OpenAI key is used. |

For example, run with a Qwen model already available on your Ollama server:

```sh
AI__Provider=ollama AI__Ollama__BaseUrl=http://127.0.0.1:11434 AI__Ollama__Model=qwen3:8b \
  dotnet run --project BlotterSync/BlotterSync.csproj --launch-profile http
```

The Ollama adapter calls only the server's chat endpoint. Start Ollama and make the chosen model available separately. See [Ollama tool calling](https://docs.ollama.com/capabilities/tool-calling) and [structured outputs](https://docs.ollama.com/capabilities/structured-outputs) for model and runtime compatibility.

In model mode, the configured provider can receive the recent conversation and the data read by its tools. OpenAI requests use `store: false`. Both providers use the same read-only tools and response validation. Chats are saved in the local audit database, independently of the incident register. Both provider adapters are tested with mock HTTP responses.

## How review works

1. Select an incident and prepare a brief, or enter an initial account in Intake assistant.
2. Compare the draft with the source, verify missing details, and accept or reject it.
3. An accepted brief records a review decision without changing the incident.
4. An accepted intake fills the filing form. You can restore it later from Agent history with **Use in filing form**, and compare it with its saved original notes. Check category, Philippine date/time, location and narrative; explicitly submit **File incident** to create a Pending record.

Agent runs have independent per-officer access checks. Administrators can summarize records they can already access; run histories remain personal. The server validates categories, location evidence and record references; officers must still check semantic accuracy. Model text is escaped before display.

## Storage and deployment

The default provider is SQLite. The existing Supabase sync remains available: set `DatabaseProvider=PostgreSQL`, configure `ConnectionStrings__DefaultConnection` or `DATABASE_URL`, and disable development demo mode. The API continues to use the local SQLite store and existing outbox sync service. Existing SQL Server configuration is also supported with `DatabaseProvider=SqlServer`.

For non-demo Development use, set `Demo__Enabled=false` and set `ConnectionStrings__LocalConnection` to your intended local database; the development configuration otherwise selects the demo file. Production uses the main configuration and has no demo login.

Production requires `Jwt__Key` (at least 32 bytes), plus `Jwt__Issuer` and `Jwt__Audience` if changing the defaults. Use a .NET-capable host, HTTPS, and a persistent private volume for `App_Data`. For a production process without a launch profile:

```sh
dotnet run --project BlotterSync/BlotterSync.csproj --no-launch-profile --environment Production
```

Runs are stored separately in `agent-demo.db` or `agent-runs.db` in `App_Data`; they are not synchronized to Supabase. Both the incident and audit databases must be backed up and access-restricted. Audit outputs and saved original intake notes contain incident text. This first version retains runs until an operator applies a retention policy; it is designed for a single server process. Restart recovery marks unfinished runs as cancelled. A production rollout should define data retention and officer provisioning; the upstream self-registration route remains enabled. The retained AutoMapper dependency also has its own production licensing requirements.

The retained backend cannot run in the Sites Cloudflare Worker runtime. This deliverable runs locally; production hosting must be configured separately.

## Verification

```sh
dotnet test BlotterSync.Tests/BlotterSync.Tests.csproj
node --check BlotterSync/wwwroot/workspace.js
# With a Development demo server running in local-rules mode:
python3 scripts/smoke-test.py
```

The HTTP smoke script first verifies demo mode, creates synthetic test accounts/records in that development database, and checks authentication, isolation, immutable reviews, drafts and explicit filing. Tests never require a cloud database or model key.

See [the implementation plan](docs/AI_IMPLEMENTATION_PLAN.md) for architecture and acceptance criteria, and [the agent API](docs/AGENT_API.md) for endpoint contracts.
