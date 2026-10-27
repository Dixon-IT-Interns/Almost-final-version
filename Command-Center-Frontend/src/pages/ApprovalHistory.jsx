import { useCallback, useEffect, useMemo, useState } from "react";
import { ChevronDown, ChevronRight, RefreshCw, Search } from "lucide-react";
import { api } from "../api";
import ApproverShell from "../components/approver/ApproverShell";
import "./ApprovalHistory.css";

const today = () => new Date().toISOString().slice(0, 10);
const daysAgo = (number) => {
  const date = new Date();
  date.setDate(date.getDate() - number);
  return date.toISOString().slice(0, 10);
};

export default function ApprovalHistory({ session, onSignOut }) {
  const [fromDate, setFromDate] = useState(daysAgo(89));
  const [toDate, setToDate] = useState(today());
  const [action, setAction] = useState("");
  const [search, setSearch] = useState("");
  const [page, setPage] = useState(1);
  const [data, setData] = useState({ rows: [], total: 0, totalPages: 0 });
  const [expandedDates, setExpandedDates] = useState(() => new Set());
  const [loading, setLoading] = useState(true);
  const [error, setError] = useState("");

  const load = useCallback(async () => {
    setLoading(true);
    setError("");
    try {
      const query = new URLSearchParams({ fromDate, toDate, page: String(page), pageSize: "25" });
      if (action) query.set("action", action);
      if (search.trim()) query.set("search", search.trim());
      setData(await api.get(`/approver/activity?${query}`));
    } catch (e) {
      setError(e.message || "Unable to load report activity.");
    } finally {
      setLoading(false);
    }
  }, [fromDate, toDate, action, search, page]);

  // eslint-disable-next-line react-hooks/set-state-in-effect
  useEffect(() => { void load(); }, [load]);

  const dateGroups = useMemo(() => {
    const groups = new Map();
    for (const row of data.rows) {
      const key = dateKey(row.at);
      if (!groups.has(key)) groups.set(key, []);
      groups.get(key).push(row);
    }
    return [...groups.entries()].map(([date, rows]) => ({ date, rows }));
  }, [data.rows]);

  const toggleDate = (date) => {
    setExpandedDates((current) => {
      const next = new Set(current);
      if (next.has(date)) next.delete(date);
      else next.add(date);
      return next;
    });
  };

  return (
    <ApproverShell user={session.user} onSignOut={onSignOut}>
      <header className="approver-page-heading">
        <div>
          <span>REVIEW AUDIT</span>
          <h1>Activity history</h1>
          <p>Submission, correction, approval and return actions by your assigned plant users.</p>
        </div>
        <button className="approver-action secondary" onClick={load} disabled={loading}>
          <RefreshCw size={14} />{loading ? "Refreshing…" : "Refresh"}
        </button>
      </header>

      {error && <div className="approver-error" role="alert">{error}</div>}
      <div className="approver-toolbar">
        <label>From<input type="date" value={fromDate} max={toDate} onChange={(event) => { setFromDate(event.target.value); setPage(1); }} /></label>
        <label>To<input type="date" value={toDate} min={fromDate} max={today()} onChange={(event) => { setToDate(event.target.value); setPage(1); }} /></label>
        <label>Action<select value={action} onChange={(event) => { setAction(event.target.value); setPage(1); }}>
          <option value="">All actions</option>
          <option value="SUBMITTED">Submitted</option>
          <option value="RESUBMITTED">Resubmitted</option>
          <option value="REJECTED">Correction requested</option>
          <option value="APPROVED">Approved</option>
        </select></label>
        <label style={{ flex: "1 1 250px" }}>Search<div className="approver-search"><Search size={14} /><input placeholder="Contributor, customer, line or model" value={search} onChange={(event) => { setSearch(event.target.value); setPage(1); }} /></div></label>
        <span className="approver-results-count">{data.total.toLocaleString("en-IN")} events</span>
      </div>

      <section className="ah-date-groups" aria-label="Activity grouped by date">
        {loading ? (
          <div className="approver-empty">Loading activity…</div>
        ) : dateGroups.length ? dateGroups.map(({ date, rows }) => {
          const expanded = expandedDates.has(date);
          return (
            <article className="ah-date-group" key={date}>
              <button
                type="button"
                className="ah-date-toggle"
                aria-expanded={expanded}
                onClick={() => toggleDate(date)}
              >
                {expanded ? <ChevronDown size={17} /> : <ChevronRight size={17} />}
                <strong>{fmtDateOnly(date)}</strong>
                <span>{rows.length} {rows.length === 1 ? "event" : "events"}</span>
              </button>
              {expanded && (
                <div className="approver-table-wrap ah-date-table">
                  <table className="approver-table">
                    <thead><tr><th>When</th><th>Action</th><th>Who</th><th>Report</th><th>Line / model</th><th>Comment</th></tr></thead>
                    <tbody>{rows.map((row, index) => (
                      <tr key={`${row.dailyReportId}-${row.action}-${row.at}-${index}`}>
                        <td><strong>{fmtDate(row.at)}</strong><small>{fmtTime(row.at)}</small></td>
                        <td><span className={`approver-status ${row.action.toLowerCase()}`}>{actionLabel(row.action)}</span></td>
                        <td>{row.actor}</td>
                        <td><strong>{row.customerName}</strong><small>{row.categoryName} · {fmtDateOnly(row.reportDate)}</small></td>
                        <td><strong>{row.lineName}</strong><small>{row.modelName || "Model unavailable"}</small></td>
                        <td>{row.comment || "—"}</td>
                      </tr>
                    ))}</tbody>
                  </table>
                </div>
              )}
            </article>
          );
        }) : (
          <div className="approver-empty"><strong>No activity found</strong><span>Try a wider date range or remove a filter.</span></div>
        )}
      </section>

      <div className="approver-pager">
        <span>Page {page} of {Math.max(1, data.totalPages || 0)}</span>
        <button disabled={page <= 1 || loading} onClick={() => setPage((current) => current - 1)}>Previous</button>
        <button disabled={page >= (data.totalPages || 1) || loading} onClick={() => setPage((current) => current + 1)}>Next</button>
      </div>
    </ApproverShell>
  );
}

function dateKey(value) {
  const date = new Date(value);
  return `${date.getFullYear()}-${String(date.getMonth() + 1).padStart(2, "0")}-${String(date.getDate()).padStart(2, "0")}`;
}

function actionLabel(action) {
  return action === "REJECTED" ? "CORRECTION REQUESTED" : action;
}

function fmtDate(value) {
  return new Date(value).toLocaleDateString("en-IN", { day: "2-digit", month: "short", year: "numeric" });
}

function fmtTime(value) {
  return new Date(value).toLocaleTimeString("en-IN", { hour: "2-digit", minute: "2-digit" });
}

function fmtDateOnly(value) {
  return new Date(`${value}T00:00:00`).toLocaleDateString("en-IN", { day: "2-digit", month: "short", year: "numeric" });
}
