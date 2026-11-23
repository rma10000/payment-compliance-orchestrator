using System.Net;
using System.Net.Http.Json;
using Xunit;

public class PipelineEvalTests
{
    private readonly HttpClient _client = new() { BaseAddress = new Uri("http://localhost:5000") };

    private record TransferResponse(
        Guid CaseId,
        string SenderId,
        string SenderName,
        decimal Amount,
        string DestinationCountry,
        int RiskScore,
        decimal Fee,
        string Status,
        string RequiredRole,
        List<string> Reasons);

    private record DecisionResponse(string Status, string? SarDraft);

    [Fact]
    public async Task StandardTransfer_AutoApprovesWithExactFee()
    {
        var res = await _client.PostAsJsonAsync("/api/evaluate", new
        {
            senderId = $"ACC-STD-{Guid.NewGuid():N}",
            senderName = "John Doe",
            amount = 450m,
            destinationCountry = "GB"
        });

        res.EnsureSuccessStatusCode();
        var body = await res.Content.ReadFromJsonAsync<TransferResponse>();

        Assert.NotNull(body);
        Assert.Equal("APPROVED", body.Status);
        Assert.Equal(6.75m, body.Fee);
        Assert.Equal("OPS_ANALYST", body.RequiredRole);
    }

    [Fact]
    public async Task WatchlistAndHighRiskCountry_BlocksOpsAnalystAndDraftsReportForMlro()
    {
        // 1. Evaluate high-risk watchlist transfer
        var evalRes = await _client.PostAsJsonAsync("/api/evaluate", new
        {
            senderId = $"ACC-WL-{Guid.NewGuid():N}",
            senderName = "Victor Boute",
            amount = 3000m,
            destinationCountry = "MM"
        });

        evalRes.EnsureSuccessStatusCode();
        var flagged = await evalRes.Content.ReadFromJsonAsync<TransferResponse>();

        Assert.NotNull(flagged);
        Assert.Equal("PENDING_REVIEW", flagged.Status);
        Assert.Equal(100, flagged.RiskScore);
        Assert.Equal(70.00m, flagged.Fee); // (3000 * 1.5%) + £25 high-risk surcharge
        Assert.Equal("SENIOR_MLRO", flagged.RequiredRole);

        // 2. Verify Ops Analyst is blocked (403 Forbidden)
        var opsAttempt = await _client.PostAsJsonAsync($"/api/cases/{flagged.CaseId}/decide", new
        {
            role = "OPS_ANALYST",
            decision = "APPROVED"
        });
        Assert.Equal(HttpStatusCode.Forbidden, opsAttempt.StatusCode);

        // 3. Verify Senior MLRO rejection generates UK NCA SAR draft
        var mlroReject = await _client.PostAsJsonAsync($"/api/cases/{flagged.CaseId}/decide", new
        {
            role = "SENIOR_MLRO",
            decision = "REJECTED"
        });
        mlroReject.EnsureSuccessStatusCode();
        var decisionBody = await mlroReject.Content.ReadFromJsonAsync<DecisionResponse>();

        Assert.NotNull(decisionBody);
        Assert.Equal("REJECTED", decisionBody.Status);
        Assert.False(string.IsNullOrWhiteSpace(decisionBody.SarDraft));
    }

    [Fact]
    public async Task SplitTransfersOver24Hours_FlagsThirdTransferAtTenThousandLimit()
    {
        var senderId = $"ACC-SPLIT-{Guid.NewGuid():N}";

        // Transfer 1 (£4,200) -> Auto-approved
        var tx1 = await (await _client.PostAsJsonAsync("/api/evaluate", new
        {
            senderId,
            senderName = "Alice Smith",
            amount = 4200m,
            destinationCountry = "GB"
        })).Content.ReadFromJsonAsync<TransferResponse>();
        Assert.Equal("APPROVED", tx1!.Status);

        // Transfer 2 (£8,400 cumulative) -> Auto-approved
        var tx2 = await (await _client.PostAsJsonAsync("/api/evaluate", new
        {
            senderId,
            senderName = "Alice Smith",
            amount = 4200m,
            destinationCountry = "GB"
        })).Content.ReadFromJsonAsync<TransferResponse>();
        Assert.Equal("APPROVED", tx2!.Status);

        // Transfer 3 (£12,600 cumulative >= £10,000 limit) -> Flagged for Senior MLRO
        var tx3 = await (await _client.PostAsJsonAsync("/api/evaluate", new
        {
            senderId,
            senderName = "Alice Smith",
            amount = 4200m,
            destinationCountry = "GB"
        })).Content.ReadFromJsonAsync<TransferResponse>();
        Assert.Equal("PENDING_REVIEW", tx3!.Status);
        Assert.Equal("SENIOR_MLRO", tx3.RequiredRole);
    }
}
