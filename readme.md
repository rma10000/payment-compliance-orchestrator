# Payment Compliance Orchestrator

A UK payment compliance and human-in-the-loop sign-off console built with **C# (.NET 10), ASP.NET Core, Microsoft Semantic Kernel, Groq, and React (TypeScript)**.

Designed for UK payment operations teams, this tool automates OFSI watchlist screening, exact `decimal` compliance fee calculations, and 24-hour rolling split-transfer checks (`£10,000` UK MLR structuring limit) while keeping humans in control of final sign-off. Low-risk payments auto-clear, while high-risk transfers route to a role-gated review queue where an `Ops Analyst` is blocked (`HTTP 403`) from overriding `Senior MLRO` approval requirements. When a `Senior MLRO` rejects a flagged payment, Semantic Kernel drafts a UK NCA Suspicious Activity Report (SAR) for review.

---

## System Architecture

```text
[React + TypeScript Console] ── REST (/api/*) ──► [ASP.NET Core .NET 10 (:5000)]
 (Served from wwwroot/)                                    │
                                 ┌─────────────────────────┼─────────────────────────┐
                                 ▼                         ▼                         ▼
                      Sanctions Name Screen      Fee & 24h Threshold Math   Semantic Kernel + Groq
                      (OFSI Watchlist Match)     (C# decimal + £10k Limit)  (Summaries & NCA SAR)
```

* **Unified Single-Port Hosting (`:5000`):** ASP.NET Core compiles and serves the React UI directly from `wwwroot/`, eliminating cross-origin (CORS) issues and secondary frontend ports.
* **Deterministic Guardrails:** Fee math (`1.5%` + `£25` high-risk corridor surcharge), `£10,000` rolling 24-hour limits, and role-based sign-off checks execute in strict C# code rather than LLM guesses.

---

## Why Two Terminals Are Used for Live Demos

When running the interactive UI alongside manual API checks, **two terminal tabs** keep the server uninterrupted:
1. **Terminal 1 (Server Host):** Runs the persistent ASP.NET Core web server (`http://localhost:5000`) serving the React console and REST API. Reusing or closing this tab shuts down the server.
2. **Terminal 2 (Manual API Checks):** Sends live HTTP requests (`POST /api/evaluate`, `POST /api/cases/{id}/decide`) against Terminal 1 without stopping it.

*(For automated `xUnit` verification, use the single-terminal background job command below.)*

---

## How to Run

### Prerequisites
* [.NET 10 SDK](https://dotnet.microsoft.com/en-us/download/dotnet/10.0)
* [Node.js 20+](https://nodejs.org)
* Free [Groq API Key](https://api.groq.com/openai/v1) saved in a root `.env` file (`GROQ_API_KEY=your_key`)

### Windows (PowerShell)

**Start the Unified React + C# Console:**
```powershell
cd compliance-ui; npm install; npm run build; cd ..
New-Item -ItemType Directory -Force -Path "ComplianceApi\wwwroot" | Out-Null
Copy-Item -Path "compliance-ui\dist\*" -Destination "ComplianceApi\wwwroot" -Recurse -Force
dotnet run --project ComplianceApi --urls "http://localhost:5000"
```

**Run Automated Tests in One Terminal:**
```powershell
$s = Start-Job { dotnet run --project "$PWD\ComplianceApi" --urls "http://localhost:5000" }; Start-Sleep 4; dotnet test; Stop-Job $s; Remove-Job $s
```

### macOS / Linux (Bash or Zsh)

**Start the Unified React + C# Console:**
```bash
cd compliance-ui && npm install && npm run build && cd ..
mkdir -p ComplianceApi/wwwroot && cp -r compliance-ui/dist/* ComplianceApi/wwwroot/
dotnet run --project ComplianceApi --urls "http://localhost:5000"
```

**Run Automated Tests in One Terminal:**
```bash
dotnet run --project ComplianceApi --urls "http://localhost:5000" &
SERVER_PID=$!
sleep 4 && dotnet test
kill $SERVER_PID
```

Open **`http://localhost:5000`** in your browser (or VS Code `Simple Browser: Show`) to use the interactive console.

---

## Test Results

Verified via `dotnet test` (`xUnit` integration suite against `http://localhost:5000`):

```text
[xUnit.net 00:00:00.00] xUnit.net VSTest Adapter v3.1.4+50e68bbb8b (64-bit .NET 10.0.15)
[xUnit.net 00:00:00.84]   Starting:    complianceApi.Tests
[xUnit.net 00:00:01.55]   Finished:    complianceApi.Tests
  complianceApi.Tests test net10.0 succeeded (3.5s)

Test summary: total: 3, failed: 0, succeeded: 3, skipped: 0, duration: 3.4s
```

| Test Case | Scenario | Verified Outcome |
| :--- | :--- | :--- |
| `StandardTransfer_AutoApprovesWithExactFee` | `£450` domestic transfer (`GB`) | Exact `1.5%` fee (`£6.75`), auto-cleared (`APPROVED`) |
| `WatchlistAndHighRiskCountry_BlocksOpsAnalystAndDraftsReportForMlro` | `Victor Boute`, `£3,000` → `MM` | `100/100` risk, `£70.00` fee (`1.5%` + `£25` surcharge), `403 Forbidden` block on `OPS_ANALYST`, UK NCA SAR draft generated on `SENIOR_MLRO` rejection |
| `SplitTransfersOver24Hours_FlagsThirdTransferAtTenThousandLimit` | `3x £4,200` within 24 hours | Transfers 1–2 (`£4.2k`, `£8.4k`) auto-approved; Transfer 3 (`£12.6k` ≥ `£10k` UK MLR limit) flagged (`PENDING_REVIEW`) |
