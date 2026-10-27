import { useCallback, useEffect, useMemo, useState } from "react";
import { Download, RefreshCw, Search, AlertTriangle, ClipboardCheck, CalendarDays, FileSpreadsheet } from "lucide-react";
import { Link } from "react-router-dom";
import { api } from "../../api";
import PortalLayout from "../../components/PortalLayout";
import "./ViewerInsights.css";

const today = new Date(Date.now() - new Date().getTimezoneOffset() * 60000).toISOString().slice(0, 10);
const modes = {
  exceptions: { eyebrow: "OPERATIONS / EXCEPTIONS", title: "Operations Exceptions", subtitle: "Prioritise open work orders, aged balances, OSD and TRC indicators." },
  comparison: { eyebrow: "ANALYTICS / COMPARISON", title: "Period Comparison", subtitle: "Compare production plan and actual output across two date ranges." },
  tracker: { eyebrow: "REPORTING / TRACKER", title: "Report Tracker", subtitle: "Review reporting status for every line on the selected date." },
  center: { eyebrow: "REPORTING / EXPORTS", title: "Report Center", subtitle: "Review report coverage and export the selected plant's data." },
};
const n = value => Number.isFinite(Number(value)) ? Number(value) : 0;
const fmt = value => n(value).toLocaleString("en-IN", { maximumFractionDigits: 2 });
const displayMetric = (line, field, value, isPercent = false) => {
  if (value === null || value === undefined || value === "")
    return line.textValues?.[field] || "—";
  const formatted = fmt(value);
  return isPercent ? `${formatted}%` : formatted;
};
const periodRange = (period, value) => {
  if (period === "year") {
    const year = Number(value);
    return { start: `${year}-01-01`, end: `${year}-12-31` };
  }
  const [year, month] = value.split("-").map(Number);
  const end = new Date(Date.UTC(year, month, 0)).toISOString().slice(0, 10);
  return { start: `${value}-01`, end };
};
const previousPeriodValue = (period, value) => {
  if (period === "year") return String(Number(value) - 1);
  const [year, month] = value.split("-").map(Number);
  return new Date(Date.UTC(year, month - 2, 1)).toISOString().slice(0, 7);
};
const groupTrend = (rows, granularity) => {
  if (granularity === "day") return rows.map(row => ({
    ...row,
    label: new Date(`${row.date}T00:00:00`).toLocaleDateString("en-IN", { day: "numeric", month: "short" }),
  }));
  const groups = new Map();
  for (const row of rows) {
    const key = row.date.slice(0, 7);
    const group = groups.get(key) || { date: key, label: new Date(`${key}-01T00:00:00`).toLocaleDateString("en-IN", { month: "short", year: "numeric" }), productionPlan: 0, productionActual: 0 };
    group.productionPlan += n(row.productionPlan);
    group.productionActual += n(row.productionActual);
    groups.set(key, group);
  }
  return [...groups.values()];
};

export default function ViewerInsights({ mode = "exceptions", session, onSignOut }) {
  const config = modes[mode] || modes.exceptions;
  const [date, setDate] = useState(today);
  const [comparisonPeriod, setComparisonPeriod] = useState("month");
  const [comparisonMonth, setComparisonMonth] = useState(today.slice(0, 7));
  const [comparisonYear, setComparisonYear] = useState(today.slice(0, 4));
  const [plantId, setPlantId] = useState("");
  const [plants, setPlants] = useState([]);
  const [customerId, setCustomerId] = useState("");
  const [customers, setCustomers] = useState([]);
  const [process, setProcess] = useState("ALL");
  const [lines, setLines] = useState([]);
  const [trendCurrent, setTrendCurrent] = useState([]);
  const [trendPrevious, setTrendPrevious] = useState([]);
  const [search, setSearch] = useState("");
  const [loading, setLoading] = useState(true);
  const [exporting, setExporting] = useState(false);
  const [error, setError] = useState("");
  const currentRange = useMemo(
    () => periodRange(comparisonPeriod, comparisonPeriod === "month" ? comparisonMonth : comparisonYear),
    [comparisonPeriod, comparisonMonth, comparisonYear],
  );
  const previousRange = useMemo(
    () => periodRange(comparisonPeriod, previousPeriodValue(
      comparisonPeriod, comparisonPeriod === "month" ? comparisonMonth : comparisonYear,
    )),
    [comparisonPeriod, comparisonMonth, comparisonYear],
  );
  const reportDate = mode === "comparison" ? currentRange.end : date;

  useEffect(() => {
    let active = true;
    api.get("/viewer/plants").then(rows => {
      if (!active) return;
      const list = Array.isArray(rows) ? rows : [];
      setPlants(list);
      const assigned = list.find(p => String(p.plantId) === String(session?.user?.plantId));
      setPlantId(String(assigned?.plantId ?? list[0]?.plantId ?? ""));
    }).catch(e => active && setError(e.message || "Unable to load plants."));
    return () => { active = false; };
  }, [session?.user?.plantId]);

  useEffect(() => {
    let active = true;
    if (!plantId) return undefined;
    api.get(`/viewer/customers?plantId=${encodeURIComponent(plantId)}`).then(rows => {
      if (active) setCustomers(Array.isArray(rows) ? rows : []);
    }).catch(e => active && setError(e.message || "Unable to load customers."));
    return () => { active = false; };
  }, [plantId]);

  const load = useCallback(async () => {
    if (!plantId || !reportDate) return;
    setLoading(true);
    setError("");
    try {
      const params = new URLSearchParams({ reportDate, plantId, process });
      if (customerId) params.set("customerId", customerId);
      const progressPromise = api.get(`/viewer/progress?${params.toString()}`);
      if (mode === "comparison") {
        const currentParams = new URLSearchParams({ startDate: currentRange.start, endDate: currentRange.end, plantId, process });
        const previousParams = new URLSearchParams({ startDate: previousRange.start, endDate: previousRange.end, plantId, process });
        if (customerId) { currentParams.set("customerId", customerId); previousParams.set("customerId", customerId); }
        const [progress, current, previous] = await Promise.all([
          progressPromise,
          api.get(`/viewer/trend?${currentParams.toString()}`),
          api.get(`/viewer/trend?${previousParams.toString()}`),
        ]);
        setLines(Array.isArray(progress?.lines) ? progress.lines : []);
        setTrendCurrent(Array.isArray(current) ? current : []);
        setTrendPrevious(Array.isArray(previous) ? previous : []);
      } else {
        const progress = await progressPromise;
        setLines(Array.isArray(progress?.lines) ? progress.lines : []);
        setTrendCurrent([]);
        setTrendPrevious([]);
      }
    } catch (e) {
      setError(e.message || "Unable to load Viewer data.");
      setLines([]); setTrendCurrent([]); setTrendPrevious([]);
    } finally { setLoading(false); }
  }, [plantId, reportDate, process, customerId, mode, currentRange, previousRange]);

  useEffect(() => {
    let active = true;
    Promise.resolve().then(() => active && load());
    return () => { active = false; };
  }, [load]);

  const visibleLines = useMemo(() => {
    const q = search.trim().toLowerCase();
    return lines.filter(line => !q || [line.lineName, line.customerName, line.process, line.modelName, line.status, line.issueDescription]
      .some(value => String(value ?? "").toLowerCase().includes(q)));
  }, [lines, search]);

  const totals = useMemo(() => ({
    plan: visibleLines.reduce((sum, x) => sum + n(x.productionPlan), 0),
    actual: visibleLines.reduce((sum, x) => sum + n(x.productionActual), 0),
    openWO: visibleLines.reduce((sum, x) => sum + n(x.openWOQty), 0),
    agedWO: visibleLines.reduce((sum, x) => sum + n(x.over7DaysWOBalanceQty), 0),
    trcAged: visibleLines.reduce((sum, x) => sum + n(x.trcLyingOver3DaysCr), 0),
    reported: visibleLines.filter(x => x.dailyReportId != null).length,
    approved: visibleLines.filter(x => String(x.status || "").toUpperCase() === "APPROVED").length,
    issues: visibleLines.filter(x => String(x.issueDescription || "").trim()).length,
  }), [visibleLines]);

  const currentTotal = trendCurrent.reduce((sum, row) => sum + n(row.productionActual), 0);
  const previousTotal = trendPrevious.reduce((sum, row) => sum + n(row.productionActual), 0);
  const delta = previousTotal ? ((currentTotal - previousTotal) / previousTotal) * 100 : null;
  const currentTrend = useMemo(
    () => groupTrend(trendCurrent, comparisonPeriod === "year" ? "month" : "day"),
    [trendCurrent, comparisonPeriod],
  );
  const previousTrend = useMemo(
    () => groupTrend(trendPrevious, comparisonPeriod === "year" ? "month" : "day"),
    [trendPrevious, comparisonPeriod],
  );

  async function exportExcel() {
    if (!plantId || !date) return;
    setExporting(true); setError("");
    try {
      const token = localStorage.getItem("dcc-token");
      const base = import.meta.env.VITE_API_URL || "http://localhost:5211/api";
      const params = new URLSearchParams({ reportDate: date, process, plantId });
      if (customerId) params.set("customerId", customerId);
      const response = await fetch(`${base}/viewer/export?${params.toString()}`, { headers: { Authorization: `Bearer ${token}` } });
      if (!response.ok) throw new Error("Excel export failed. Check your session and API URL.");
      const blob = await response.blob();
      const url = URL.createObjectURL(blob);
      const a = document.createElement("a"); a.href = url; a.download = `Dixon_Viewer_${date.replaceAll("-", "")}.xlsx`; a.click();
      URL.revokeObjectURL(url);
    } catch (e) { setError(e.message || "Export failed."); }
    finally { setExporting(false); }
  }

  return <PortalLayout user={session?.user} onSignOut={onSignOut}><div className="viewer-insights-page">
    <main className="vi-main">
      <div className="vi-breadcrumb"><Link to="/viewer">Viewer Dashboard</Link><span>/</span><span>{config.title}</span></div>
      <section className="vi-heading">
        <div><span className="vi-eyebrow">{config.eyebrow}</span><h1>{config.title}</h1><p>{config.subtitle}</p></div>
        <div className="vi-actions"><button className="vi-secondary" onClick={load} disabled={loading}><RefreshCw size={16}/>{loading ? "Refreshing…" : "Refresh"}</button>{mode === "center" && <button className="vi-primary" onClick={exportExcel} disabled={exporting || !plantId}><Download size={16}/>{exporting ? "Exporting…" : "Export Excel"}</button>}</div>
      </section>
      <section className="vi-filters">
        <label>Plant<select value={plantId} onChange={e => {
          setCustomerId("");
          setCustomers([]);
          setPlantId(e.target.value);
        }}><option value="">Select plant</option>{plants.map(p => <option key={p.plantId} value={p.plantId}>{p.plantName}</option>)}</select></label>
        {mode === "comparison" ? <>
          <label>Compare by<select value={comparisonPeriod} onChange={e => setComparisonPeriod(e.target.value)}><option value="month">Month vs previous month</option><option value="year">Year vs previous year</option></select></label>
          {comparisonPeriod === "month"
            ? <label>Month<input type="month" value={comparisonMonth} onChange={e => e.target.value && setComparisonMonth(e.target.value)} /></label>
            : <label>Year<input type="number" min="2000" max="2200" value={comparisonYear} onChange={e => {
              const value = Number(e.target.value);
              if (value >= 2000 && value <= 2200) setComparisonYear(e.target.value);
            }} /></label>}
        </> : <label>Report date<input type="date" value={date} onChange={e => setDate(e.target.value)} /></label>}
        <label>Brand<select value={customerId} onChange={e => setCustomerId(e.target.value)}><option value="">All brands</option>{customers.map(c => <option key={c.customerId} value={c.customerId}>{c.customerName}</option>)}</select></label>
        <label>Process<select value={process} onChange={e => setProcess(e.target.value)}><option value="ALL">FATP + SMT</option><option value="FATP">FATP</option><option value="SMT">SMT</option></select></label>
        <label className="vi-search">Search lines<span><Search size={16}/><input value={search} onChange={e => setSearch(e.target.value)} placeholder="Line, model, issue…" /></span></label>
      </section>
      {error && <div className="vi-error" role="alert">{error}</div>}
      {mode === "exceptions" && <>
        <section className="vi-kpis"><Kpi icon={<AlertTriangle size={18}/>} label="Open WO quantity" value={fmt(totals.openWO)}/><Kpi icon={<CalendarDays size={18}/>} label="WO over 7 days" value={fmt(totals.agedWO)}/><Kpi icon={<ClipboardCheck size={18}/>} label="TRC lying over 3 days" value={fmt(totals.trcAged)}/><Kpi icon={<AlertTriangle size={18}/>} label="Lines with issue notes" value={fmt(totals.issues)}/></section>
        <Panel title="Operational exceptions" subtitle="Rows are flagged when an issue or aged operational quantity is present.">
          <div className="vi-table-wrap"><table className="vi-table"><thead><tr><th>Line</th><th>Customer</th><th>Open WO</th><th>WO &gt; 7 days</th><th>OS&D date %</th><th>OS&D MTD %</th><th>TRC failure %</th><th>TRC &gt; 3 days</th><th>Issue</th></tr></thead><tbody>{visibleLines.filter(x => n(x.openWOQty) || n(x.over7DaysWOBalanceQty) || n(x.trcLyingOver3DaysCr) || n(x.osdReportingDateValue) || n(x.osdMtdValue) || n(x.trcOverallFailureInflowPercent) || ["openWOQty","over7DaysWOBalanceQty","trcLyingOver3DaysCr","osdReportingDateValue","osdMtdValue","trcOverallFailureInflowPercent"].some(field => x.textValues?.[field]) || x.issueDescription).map((x,i)=><tr key={x.dailyReportId ?? `${x.lineId}-${i}`}><td className="vi-strong">{x.lineName || "—"}</td><td>{x.customerName || "—"}</td><td>{displayMetric(x,"openWOQty",x.openWOQty)}</td><td><Flag value={x.over7DaysWOBalanceQty} textValue={x.textValues?.over7DaysWOBalanceQty}/></td><td>{displayMetric(x,"osdReportingDatePercent",x.osdReportingDatePercent,true)}</td><td>{displayMetric(x,"osdMtdPercent",x.osdMtdPercent,true)}</td><td>{displayMetric(x,"trcOverallFailureInflowPercent",x.trcOverallFailureInflowPercent,true)}</td><td><Flag value={x.trcLyingOver3DaysCr} textValue={x.textValues?.trcLyingOver3DaysCr}/></td><td className="vi-issue">{x.issueDescription || "—"}</td></tr>)}</tbody></table></div>
          {visibleLines.length && !visibleLines.some(x => n(x.openWOQty) || n(x.over7DaysWOBalanceQty) || n(x.trcLyingOver3DaysCr) || n(x.osdReportingDateValue) || n(x.osdMtdValue) || n(x.trcOverallFailureInflowPercent) || x.issueDescription) ? <Empty>No operational exceptions in the selected records.</Empty> : null}
        </Panel>
      </>}
      {mode === "comparison" && <>
        <section className="vi-kpis"><Kpi label={`Selected ${comparisonPeriod} actual`} value={fmt(currentTotal)}/><Kpi label={`Previous ${comparisonPeriod} actual`} value={fmt(previousTotal)}/><Kpi label="Period change" value={delta == null ? "—" : `${delta >= 0 ? "+" : ""}${fmt(delta)}%`}/><Kpi label={`Selected ${comparisonPeriod} plan`} value={fmt(trendCurrent.reduce((sum, row) => sum + n(row.productionPlan), 0))}/></section>
        <Panel title={`${comparisonPeriod === "month" ? "Monthly" : "Yearly"} production comparison`} subtitle={`Current: ${currentRange.start} to ${currentRange.end} · Previous: ${previousRange.start} to ${previousRange.end} · ${customerId ? "Selected brand" : "All brands"}`}>
          <div className="vi-compare-grid"><CompareColumn title={comparisonPeriod === "month" ? "Current month · daily" : "Current year · monthly"} rows={currentTrend}/><CompareColumn title={comparisonPeriod === "month" ? "Previous month · daily" : "Previous year · monthly"} rows={previousTrend}/></div>
        </Panel>
      </>}
      {mode === "tracker" && <>
        <section className="vi-kpis"><Kpi label="Lines in scope" value={fmt(visibleLines.length)}/><Kpi label="Reported lines" value={fmt(totals.reported)}/><Kpi label="Approved records" value={fmt(totals.approved)}/><Kpi label="Not reported" value={fmt(Math.max(0, visibleLines.length - totals.reported))}/></section>
        <Panel title="Daily reporting status" subtitle="Status comes directly from the selected report date's Viewer API response."><div className="vi-table-wrap"><table className="vi-table"><thead><tr><th>Line</th><th>Customer</th><th>Process</th><th>Model</th><th>Status</th><th>Production plan</th><th>Production actual</th><th>Issue note</th></tr></thead><tbody>{visibleLines.map((x,i)=><tr key={x.dailyReportId ?? `${x.lineId}-${i}`}><td className="vi-strong">{x.lineName || "—"}</td><td>{x.customerName || "—"}</td><td>{x.process || "—"}</td><td>{x.modelName || "—"}</td><td><span className={`vi-status ${String(x.status).toUpperCase() === "APPROVED" ? "is-approved" : String(x.status).toUpperCase() === "NOT_REPORTED" ? "is-missing" : ""}`}>{x.status || "UNKNOWN"}</span></td><td>{displayMetric(x,"productionPlan",x.productionPlan)}</td><td>{displayMetric(x,"productionActual",x.productionActual)}</td><td className="vi-issue">{x.issueDescription || "—"}</td></tr>)}</tbody></table></div>{!visibleLines.length && <Empty>No lines found for the selected filters.</Empty>}</Panel>
      </>}
      {mode === "center" && <>
        <section className="vi-kpis"><Kpi icon={<FileSpreadsheet size={18}/>} label="Lines in export scope" value={fmt(visibleLines.length)}/><Kpi label="Production plan" value={fmt(totals.plan)}/><Kpi label="Production actual" value={fmt(totals.actual)}/><Kpi label="Reported records" value={fmt(totals.reported)}/></section>
        <Panel title="Export details" subtitle="Exports use the supplied daily report workbook as a style-preserving template."><div className="vi-export-info"><div className="vi-export-icon"><FileSpreadsheet size={25}/></div><div><h3>Daily Viewer workbook</h3><p>Fills the first daily-report sheet with selected-day production, quality, OS&D, overtime, work-order and TRC data. Other sheets retain their layout and styling without carrying over the template's sample values.</p><span>Plant: {plants.find(p => String(p.plantId) === String(plantId))?.plantName || "Select a plant"} · Date: {date} · Brand: {customerId ? customers.find(c => String(c.customerId) === String(customerId))?.customerName : "All brands"} · Process: {process}</span></div><button className="vi-primary" onClick={exportExcel} disabled={exporting || !plantId}><Download size={16}/>{exporting ? "Exporting…" : "Download .xlsx"}</button></div></Panel>
        <Panel title="Data preview" subtitle="Preview of the rows included for the selected date."><div className="vi-table-wrap"><table className="vi-table"><thead><tr><th>Line</th><th>Customer</th><th>Model</th><th>Status</th><th>Plan</th><th>Actual</th></tr></thead><tbody>{visibleLines.slice(0,20).map((x,i)=><tr key={x.dailyReportId ?? `${x.lineId}-${i}`}><td className="vi-strong">{x.lineName || "—"}</td><td>{x.customerName || "—"}</td><td>{x.modelName || "—"}</td><td>{x.status || "UNKNOWN"}</td><td>{fmt(x.productionPlan)}</td><td>{fmt(x.productionActual)}</td></tr>)}</tbody></table></div>{!visibleLines.length && <Empty>No rows available for preview.</Empty>}</Panel>
      </>}
      {loading && <div className="vi-loading">Loading Viewer records…</div>}
      <p className="vi-footnote">Read-only Viewer access. Data is limited to the plant and customer scope allowed by the existing backend authorization.</p>
    </main>
  </div></PortalLayout>;
}
function Kpi({ icon, label, value }) { return <article className="vi-kpi">{icon && <span className="vi-kpi-icon">{icon}</span>}<span>{label}</span><strong>{value}</strong></article>; }
function Panel({ title, subtitle, children }) { return <section className="vi-panel"><div className="vi-panel-heading"><div><h2>{title}</h2><p>{subtitle}</p></div></div>{children}</section>; }
function Empty({ children }) { return <div className="vi-empty">{children}</div>; }
function Flag({ value, textValue }) { return <span className={`vi-flag ${n(value) > 0 ? "is-flagged" : ""}`}>{value == null ? textValue || "—" : fmt(value)}</span>; }
function CompareColumn({ title, rows }) { const max = Math.max(1, ...rows.map(r => n(r.productionActual))); return <div className="vi-period"><h3>{title}</h3>{rows.length ? rows.map(row => <div className="vi-period-row" key={row.date}><div><span>{row.label || row.date}</span><strong>Plan {fmt(row.productionPlan)} · Actual {fmt(row.productionActual)}</strong></div><div className="vi-period-track"><span style={{width:`${Math.min(100, n(row.productionActual) / max * 100)}%`}}/></div></div>) : <Empty>No production trend data for this period.</Empty>}</div>; }
