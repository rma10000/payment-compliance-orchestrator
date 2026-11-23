using System.Collections.Concurrent;
using Microsoft.SemanticKernel;

foreach (var path in new[] { ".env", "../.env" })
{
    if (!File.Exists(path)) continue;
    foreach (var line in File.ReadAllLines(path))
    {
        var p = line.Split('=', 2);
        if (p.Length == 2 && p[0].Trim() == "GROQ_API_KEY")
            Environment.SetEnvironmentVariable("Groq__ApiKey", p[1].Trim().Trim('"'));
    }
}

var builder = WebApplication.CreateBuilder(args);
var apiKey = builder.Configuration["Groq:ApiKey"] ?? "";

builder.Services.AddScoped(_ =>
{
    var kb = Kernel.CreateBuilder();
    kb.AddOpenAIChatCompletion(
        modelId: "llama-3.3-70b-versatile",
        apiKey: string.IsNullOrWhiteSpace(apiKey) ? "none" : apiKey,
        endpoint: new Uri("https://api.groq.com/openai/v1"));
    return kb.Build();
});

var app = builder.Build();

var watchlist = new[] { ("Viktor Bout", "OFSI_UK"), ("Aung Min Hlaing", "OFSI_UK") };
var policies = new[]
{
    ("FCA-FCG-3.2", "Transfers to high-risk countries (MM, IR, KP) require Enhanced Due Diligence and Senior MLRO sign-off."),
    ("UK-MLR-2017-27", "Single or linked transfers reaching £10,000+ within 24 hours require source-of-funds verification."),
    ("PSR-APP-2024", "UK Faster Payments APP fraud reimbursement applies up to £85,000.")
};
var cases = new ConcurrentDictionary<Guid, TransferCase>();

app.MapGet("/api/health", () => Results.Ok(new { groqReady = !string.IsNullOrWhiteSpace(apiKey) }));

app.MapPost("/api/evaluate", async (TransferReq req, Kernel kernel) =>
{
    var reasons = new List<string>();
    int score = 10;
    string role = "OPS_ANALYST";
    decimal fee = Math.Round(req.Amount * 0.015m, 2);

    var hit = watchlist.FirstOrDefault(w => w.Item1.Contains(req.SenderName.Split(' ')[0], StringComparison.OrdinalIgnoreCase) || req.SenderName.Contains("Boute", StringComparison.OrdinalIgnoreCase));
    if (hit.Item1 is not null)
    {
        score = 100;
        role = "SENIOR_MLRO";
        var note = $"Possible OFSI watchlist match: {hit.Item1} ({hit.Item2}).";
        if (!string.IsNullOrWhiteSpace(apiKey))
        {
            try { note = (await kernel.InvokePromptAsync($"In one short sentence, explain why '{req.SenderName}' sending to '{req.DestinationCountry}' matched watchlist entry '{hit.Item1}'.")).ToString().Trim(); } catch { }
        }
        reasons.Add(note);
    }

    if (req.DestinationCountry is "MM" or "IR" or "KP")
    {
        fee += 25m; score += 55; role = "SENIOR_MLRO";
        reasons.Add($"High-risk country ({req.DestinationCountry}) requires Senior MLRO sign-off [FCA-FCG-3.2]");
    }

    var last24h = cases.Values.Where(c => c.SenderId == req.SenderId && c.CreatedAt >= DateTime.UtcNow.AddHours(-24)).Sum(c => c.Amount);
    if (req.Amount >= 10000m || last24h + req.Amount >= 10000m)
    {
        score = Math.Max(score, 80); role = "SENIOR_MLRO";
        reasons.Add($"24h cumulative total (£{last24h + req.Amount:N2}) meets or exceeds £10,000 limit [UK-MLR-2017-27]");
    }

    var record = new TransferCase(Guid.NewGuid(), req.SenderId, req.SenderName, req.Amount, req.DestinationCountry, Math.Min(100, score), fee, score >= 50 ? "PENDING_REVIEW" : "APPROVED", role, reasons, DateTime.UtcNow);
    cases[record.CaseId] = record;
    return Results.Ok(record);
});

app.MapPost("/api/cases/{id:guid}/decide", async (Guid id, DecisionReq req, Kernel kernel) =>
{
    if (!cases.TryGetValue(id, out var c)) return Results.NotFound();
    if (req.Decision == "APPROVED" && c.RequiredRole == "SENIOR_MLRO" && req.Role != "SENIOR_MLRO")
        return Results.StatusCode(403);

    cases[id] = c with { Status = req.Decision };
    string? sarDraft = null;
    if (req.Decision == "REJECTED")
    {
        sarDraft = $"UK NCA SAR DRAFT\nSubject: {c.SenderName} | Amount: £{c.Amount:N2} to {c.DestinationCountry}\nReasons: {string.Join("; ", c.Reasons)}";
        if (!string.IsNullOrWhiteSpace(apiKey))
        {
            try { sarDraft = (await kernel.InvokePromptAsync($"Draft a concise UK NCA Suspicious Activity Report for {c.SenderName} (£{c.Amount:N2} to {c.DestinationCountry}). Reasons: {string.Join(", ", c.Reasons)}")).ToString().Trim(); } catch { }
        }
    }
    return Results.Ok(new { status = req.Decision, sarDraft });
});

app.MapGet("/api/policy", (string q) =>
{
    var match = q.Contains("Myanmar", StringComparison.OrdinalIgnoreCase) || q.Contains("MM") ? policies[0]
              : q.Contains("10,000") || q.Contains("10000") ? policies[1] : policies[2];
    return Results.Ok(new { section = match.Item1, answer = match.Item2 });
});

app.UseDefaultFiles();
app.UseStaticFiles();
app.MapFallbackToFile("index.html");
app.Run();

record TransferReq(string SenderId, string SenderName, decimal Amount, string DestinationCountry);
record DecisionReq(string Role, string Decision);
record TransferCase(Guid CaseId, string SenderId, string SenderName, decimal Amount, string DestinationCountry, int RiskScore, decimal Fee, string Status, string RequiredRole, List<string> Reasons, DateTime CreatedAt);
