import { useEffect, useMemo, useState } from "react";
import {
  CalendarDays, ChevronDown, ChevronRight, ClipboardList, ExternalLink, Filter,
  RefreshCw, Search, Trash2,
} from "lucide-react";
import { useNavigate } from "react-router-dom";
import { api } from "../../api";
import ContributorLayout from "../../components/contributor/ContributorLayout";
import "./ContributorReports.css";

const STATUS_ORDER = ["DRAFT", "REJECTED", "SUBMITTED", "APPROVED"];

export default function ContributorReports({ session, onSignOut }) {
  const navigate = useNavigate();
  const [rows, setRows] = useState([]);
  const [customers, setCustomers] = useState([]);
  const [status, setStatus] = useState("");
  const [search, setSearch] = useState("");
  const [expandedDates, setExpandedDates] = useState(() => new Set());
  const [loading, setLoading] = useState(true);
  const [error, setError] = useState("");
  const [deletingId, setDeletingId] = useState(null);

  const load = async () => {
    setLoading(true);
    setError("");
    try {
      const [result, catalog] = await Promise.all([
        api.get(`/contributor/submissions?days=90${status ? `&status=${status}` : ""}`),
        api.get("/catalog/customers"),
      ]);
      setRows(Array.isArray(result) ? result : []);
      setCustomers(Array.isArray(catalog) ? catalog : []);
    } catch (e) {
      setError(e.message || "Unable to load submissions.");
    } finally {
      setLoading(false);
    }
  };

  const deleteDraft = async (row) => {
    if (!["DRAFT", "REJECTED"].includes(row.status)) return;
    const reportName = [row.customerName, row.categoryName, row.lineName, row.modelName]
      .filter(Boolean)
      .join(" / ");
    if (!window.confirm(
      `Delete the ${row.status.toLowerCase()} report for ${reportName} on ${formatDate(row.reportDate)}? This cannot be undone.`
    )) return;

    setDeletingId(row.dailyReportId);
    setError("");
    try {
      await api.delete(`/daily-reports/draft/${row.dailyReportId}`);
      setRows((current) => current.filter((item) => item.dailyReportId !== row.dailyReportId));
    } catch (e) {
      setError(e.message || "Unable to delete the draft.");
    } finally {
      setDeletingId(null);
    }
  };

  // eslint-disable-next-line react-hooks/set-state-in-effect, react-hooks/exhaustive-deps
  useEffect(() => { load(); }, [status]);

  const dailyReports = useMemo(() => {
    const searchTerm = search.trim().toLowerCase();
    const filteredRows = rows.filter((row) => (
      !searchTerm
      || `${row.reportDate} ${row.customerName} ${row.categoryName} ${row.lineName} ${row.modelName || ""} ${row.status}`
        .toLowerCase()
        .includes(searchTerm)
    ));
    const grouped = new Map();

    for (const row of filteredRows) {
      const date = row.reportDate;
      if (!grouped.has(date)) grouped.set(date, []);
      grouped.get(date).push(row);
    }

    return [...grouped.entries()]
      .sort(([dateA], [dateB]) => dateB.localeCompare(dateA))
      .map(([date, reportRows]) => {
        const statusCounts = STATUS_ORDER
          .map((item) => ({ status: item, count: reportRows.filter((row) => row.status === item).length }))
          .filter((item) => item.count > 0);
        const scopes = [...new Map(reportRows.map((row) => [
          `${row.customerId || normalizeName(row.customerName)}:${row.categoryId || normalizeName(row.categoryName)}`,
          resolveReportScope(row, customers),
        ])).values()];
        return { date, rows: reportRows, statusCounts, scopes: scopes.filter(Boolean) };
      });
  }, [rows, search, customers]);

  const toggleDate = (date) => {
    setExpandedDates((current) => {
      const next = new Set(current);
      if (next.has(date)) next.delete(date);
      else next.add(date);
      return next;
    });
  };

  return (
    <ContributorLayout user={session.user} onSignOut={onSignOut}>
      <main className="contributor-reports">
        <div className="cr-head">
          <div>
            <span>REPORT CENTER</span>
            <h1>My daily reports</h1>
            <p>Reports are grouped by date. Open a date to view its lines and models.</p>
          </div>
          <button type="button" onClick={load} disabled={loading}>
            <RefreshCw size={15} className={loading ? "cr-refreshing" : ""} /> Refresh
          </button>
        </div>

        <div className="cr-toolbar">
          <div className="cr-search">
            <Search size={16} />
            <input
              placeholder="Search customer, line or model"
              value={search}
              onChange={(event) => setSearch(event.target.value)}
            />
          </div>
          <div className="cr-filter">
            <Filter size={15} />
            <select value={status} onChange={(event) => setStatus(event.target.value)}>
              <option value="">All statuses</option>
              <option value="DRAFT">Draft</option>
              <option value="SUBMITTED">Submitted</option>
              <option value="APPROVED">Approved</option>
              <option value="REJECTED">Rejected</option>
            </select>
          </div>
        </div>

        {error && <div className="cr-error">{error}</div>}

        {loading ? (
          <section className="cr-daily-list">
            <div className="cr-empty">Loading daily reports…</div>
          </section>
        ) : dailyReports.length ? (
          <section className="cr-daily-list" aria-label="Daily reports grouped by date">
            {dailyReports.map((dailyReport) => {
              const isExpanded = expandedDates.has(dailyReport.date);
              return (
                <article className="cr-day-card" key={dailyReport.date}>
                  <div className="cr-day-summary">
                    <div className="cr-day-date">
                      <span className="cr-calendar-icon"><CalendarDays size={18} /></span>
                      <div>
                        <h2>{formatDate(dailyReport.date)}</h2>
                        <p>{dailyReport.rows.length} {dailyReport.rows.length === 1 ? "line / model entry" : "line / model entries"} · {dailyReport.scopes.join(", ")}</p>
                      </div>
                    </div>
                    <div className="cr-day-statuses">
                      {dailyReport.statusCounts.map(({ status: itemStatus, count }) => (
                        <span className={`cr-status ${itemStatus.toLowerCase()}`} key={itemStatus}>
                          {count} {itemStatus.toLowerCase()}
                        </span>
                      ))}
                    </div>
                    <button
                      className="cr-day-toggle"
                      type="button"
                      aria-expanded={isExpanded}
                      aria-label={`${isExpanded ? "Hide" : "Show"} report entries for ${formatDate(dailyReport.date)}`}
                      onClick={() => toggleDate(dailyReport.date)}
                    >
                      {isExpanded ? <ChevronDown size={16} /> : <ChevronRight size={16} />}
                      {isExpanded ? "Hide details" : "View details"}
                    </button>
                  </div>

                  {isExpanded && (
                    <div className="cr-day-details">
                      <div className="cr-report-links">
                        {!dailyReport.scopes.length && (
                          <p className="cr-scope-warning" role="status">
                            Could not match this report to an active brand/category. Refresh the page or contact an administrator to check its catalog configuration.
                          </p>
                        )}
                        {dailyReport.scopes.map((scope) => (
                          <button
                            type="button"
                            key={`${scope.customerId}:${scope.categoryId}`}
                            disabled={!scope.customerId || !scope.categoryId}
                            onClick={() => navigate(
                              `/contributor/brands/${scope.customerId}/categories/${scope.categoryId}/report?date=${dailyReport.date}&uploadType=all`
                            )}
                          >
                            <ExternalLink size={14} />
                            <span>
                              <strong>{scope.customerName} · {scope.categoryName}</strong>
                              <small>Open full daily report for {formatDate(dailyReport.date)}</small>
                            </span>
                          </button>
                        ))}
                      </div>
                      <table>
                        <thead>
                          <tr>
                            <th>Customer / category</th>
                            <th>Line / model</th>
                            <th>Status</th>
                            <th>Last update</th>
                            <th>Comment</th>
                            <th>Action</th>
                          </tr>
                        </thead>
                        <tbody>
                          {dailyReport.rows.map((row) => (
                            <tr key={row.dailyReportId}>
                              <td><strong>{row.customerName}</strong><small>{row.categoryName}</small></td>
                              <td>{row.lineName}<small>{[row.sapLocation, row.modelName].filter(Boolean).join(" · ") || "—"}</small></td>
                              <td><span className={`cr-status ${row.status.toLowerCase()}`}>{row.status}</span></td>
                              <td>{row.updatedAt ? formatDateTime(row.updatedAt) : "—"}</td>
                              <td className="cr-comment">{row.rejectionReason || "—"}</td>
                              <td>
                                {["DRAFT", "REJECTED"].includes(row.status) && (
                                  <button
                                    className="cr-delete"
                                    type="button"
                                    title="Delete draft"
                                    aria-label={`Delete ${row.status.toLowerCase()} report for ${row.modelName || row.lineName}`}
                                    disabled={deletingId === row.dailyReportId}
                                    onClick={() => deleteDraft(row)}
                                  >
                                    <Trash2 size={15} />
                                    <span>{deletingId === row.dailyReportId ? "Deleting…" : "Delete"}</span>
                                  </button>
                                )}
                              </td>
                            </tr>
                          ))}
                        </tbody>
                      </table>
                    </div>
                  )}
                </article>
              );
            })}
          </section>
        ) : (
          <section className="cr-daily-list">
            <div className="cr-empty">
              <ClipboardList size={24} />
              <strong>No submissions found</strong>
              <span>Change the filter or start a new daily report.</span>
            </div>
          </section>
        )}
      </main>
    </ContributorLayout>
  );
}

function formatDate(value) {
  return new Date(`${value}T00:00:00`).toLocaleDateString("en-IN", {
    day: "2-digit",
    month: "short",
    year: "numeric",
  });
}

function formatDateTime(value) {
  return new Date(value).toLocaleString("en-IN", {
    day: "2-digit",
    month: "short",
    hour: "2-digit",
    minute: "2-digit",
  });
}

function normalizeName(value) {
  return String(value || "").trim().toLocaleLowerCase();
}

function resolveReportScope(row, customers) {
  const customer = customers.find((item) => (
    Number(item.customerId) === Number(row.customerId)
    || normalizeName(item.customerName) === normalizeName(row.customerName)
  ));
  const category = customer?.categories?.find((item) => (
    Number(item.categoryId) === Number(row.categoryId)
    || normalizeName(item.categoryName) === normalizeName(row.categoryName)
  ));
  if (!customer || !category) return null;
  return {
    customerId: customer.customerId,
    categoryId: category.categoryId,
    customerName: customer.customerName,
    categoryName: category.categoryName,
  };
}
