import React, { useEffect, useState } from "react";

export default function App() {
  const [status, setStatus] = useState("Checking service...");
  const [role, setRole] = useState<"OPS_ANALYST" | "SENIOR_MLRO">("OPS_ANALYST");
  const [cases, setCases] = useState<any[]>([]);
  const [selected, setSelected] = useState<any>(null);
  const [brief, setBrief] = useState<string[]>([]);
  const [sar, setSar] = useState("");
  const [query, setQuery] = useState("Rules for Myanmar or transfers over £10,000?");
  const [policy, setPolicy] = useState<any>(null);
  const [msg, setMsg] = useState("");

  useEffect(() => {
    fetch("/api/health").then(r => r.json()).then(d => setStatus(`Online | Groq Key: ${d.groqReady ? "Loaded" : "Fallback Mode"}`)).catch(() => setStatus("Offline"));
  }, []);

  const runTransfer = async (senderId: string, senderName: string, amount: number, destinationCountry: string) => {
    const res = await fetch("/api/evaluate", {
      method: "POST",
      headers: { "Content-Type": "application/json" },
      body: JSON.stringify({ senderId, senderName, amount, destinationCountry }),
    });
    const data = await res.json();
    setCases(prev => [data, ...prev]);
    setSelected(data);
    setBrief([]);
    setSar("");
    setMsg(`Checked ${senderName}: Risk ${data.riskScore}/100 (${data.status})`);
  };

  const decide = async (decision: "APPROVED" | "REJECTED") => {
    if (!selected) return;
    const res = await fetch(`/api/cases/${selected.caseId}/decide`, {
      method: "POST",
      headers: { "Content-Type": "application/json" },
      body: JSON.stringify({ role, decision }),
    });
    if (res.status === 403) return setMsg("Blocked: Senior MLRO sign-off required.");
    const data = await res.json();
    setCases(prev => prev.map(c => (c.caseId === selected.caseId ? { ...c, status: decision } : c)));
    setSelected({ ...selected, status: decision });
    setSar(data.sarDraft || "");
    setMsg(`Case ${decision.toLowerCase()} by ${role}.`);
  };

  const searchPolicy = async () => {
    const res = await fetch(`/api/policy?q=${encodeURIComponent(query)}`);
    if (res.ok) setPolicy(await res.json());
  };

  return (
    <div style={{ fontFamily: "system-ui, sans-serif", maxWidth: 960, margin: "0 auto", padding: 20, color: "#0f172a" }}>
      <div style={{ display: "flex", justifyContent: "space-between", alignItems: "center", borderBottom: "1px solid #cbd5e1", paddingBottom: 10 }}>
        <div>
          <h2 style={{ margin: 0 }}>UK Payment Compliance & Sign-Off Console</h2>
          <small style={{ color: "#475569" }}>{status}</small>
        </div>
        <select value={role} onChange={e => setRole(e.target.value as any)} style={{ padding: 6 }}>
          <option value="OPS_ANALYST">Ops Analyst</option>
          <option value="SENIOR_MLRO">Senior MLRO</option>
        </select>
      </div>

      {msg && <div style={{ marginTop: 10, padding: 8, background: "#e0f2fe", color: "#075985", borderRadius: 4, fontSize: 13 }}>{msg}</div>}

      <div style={{ display: "flex", gap: 8, marginTop: 14, flexWrap: "wrap" }}>
        <button onClick={() => runTransfer("ACC-9", "Victor Boute", 3000, "MM")} style={btn("#b91c1c")}>Test Watchlist ("Victor Boute")</button>
        <button onClick={() => runTransfer("ACC-SPLIT", "Alice Smith", 4200, "GB")} style={btn("#b45309")}>Test £4,200 Split (Run 3x)</button>
        <button onClick={() => runTransfer("ACC-OK", "John Doe", 450, "GB")} style={btn("#15803d")}>Test Standard £450</button>
      </div>

      <div style={{ display: "grid", gridTemplateColumns: "1fr 1fr", gap: 16, marginTop: 16 }}>
        <div style={box}>
          <h4 style={{ marginTop: 0 }}>Transfer Queue ({cases.length})</h4>
          {cases.map(c => (
            <div key={c.caseId} onClick={() => { setSelected(c); setBrief([]); setSar(""); }} style={{ padding: 10, marginBottom: 8, border: selected?.caseId === c.caseId ? "2px solid #1d4ed8" : "1px solid #cbd5e1", borderRadius: 4, cursor: "pointer" }}>
              <div style={{ display: "flex", justifyContent: "space-between", fontWeight: 600, fontSize: 14 }}>
                <span>{c.senderName} → {c.destinationCountry}</span>
                <span>£{c.amount} (Fee: £{c.fee})</span>
              </div>
              <div style={{ fontSize: 12, marginTop: 4 }}>Risk: {c.riskScore}/100 | {c.status} | Req: {c.requiredRole}</div>
              {c.reasons.map((r: string, i: number) => <div key={i} style={{ fontSize: 12, color: "#991b1b", marginTop: 2 }}>• {r}</div>)}
            </div>
          ))}
        </div>

        <div style={box}>
          <h4 style={{ marginTop: 0 }}>Reviewer Sign-Off</h4>
          {selected ? (
            <div style={{ fontSize: 13 }}>
              <p><strong>{selected.senderName}</strong> — £{selected.amount} ({selected.status})</p>
              <div style={{ display: "flex", gap: 6, flexWrap: "wrap", marginBottom: 10 }}>
                <button onClick={() => setBrief(role === "SENIOR_MLRO" ? ["Verify source-of-funds", "Clear OFSI watchlist match", "Log MLRO sign-off"] : ["Check ID & invoice", "Escalate >£10k or watchlist hits to MLRO"])} style={btn("#1d4ed8")}>Role Checklist</button>
                <button onClick={() => decide("APPROVED")} style={btn("#15803d")}>Approve</button>
                <button onClick={() => decide("REJECTED")} style={btn("#b91c1c")}>Reject & Draft NCA Report</button>
              </div>
              {brief.length > 0 && <ul style={{ background: "#f1f5f9", padding: "8px 24px", borderRadius: 4 }}>{brief.map((b, i) => <li key={i}>{b}</li>)}</ul>}
              {sar && <textarea value={sar} onChange={e => setSar(e.target.value)} rows={5} style={{ width: "100%", marginTop: 8, fontFamily: "monospace", fontSize: 12 }} />}
            </div>
          ) : <p style={{ fontSize: 13, color: "#64748b" }}>Select a transfer to review.</p>}
        </div>
      </div>

      <div style={{ ...box, marginTop: 16 }}>
        <h4 style={{ marginTop: 0 }}>UK Policy Rule Lookup</h4>
        <div style={{ display: "flex", gap: 8 }}>
          <input value={query} onChange={e => setQuery(e.target.value)} style={{ flex: 1, padding: 6 }} />
          <button onClick={searchPolicy} style={btn("#0369a1")}>Search</button>
        </div>
        {policy && <p style={{ fontSize: 13, marginBottom: 0 }}><strong>[{policy.section}]</strong> {policy.answer}</p>}
      </div>
    </div>
  );
}

const box: React.CSSProperties = { border: "1px solid #cbd5e1", borderRadius: 6, padding: 14, background: "#fff" };
const btn = (bg: string): React.CSSProperties => ({ background: bg, color: "#fff", border: "none", padding: "7px 12px", borderRadius: 4, cursor: "pointer", fontWeight: 600, fontSize: 13 });
