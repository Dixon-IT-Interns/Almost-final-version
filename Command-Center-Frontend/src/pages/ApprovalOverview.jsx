import { useCallback, useEffect, useState } from "react";
import { Activity, CheckCircle2, Clock3, FileText, RefreshCw, XCircle } from "lucide-react";
import { Link } from "react-router-dom";
import { api } from "../api";
import ApproverShell from "../components/approver/ApproverShell";

const today=()=>new Date().toISOString().slice(0,10);
const daysAgo=(n)=>{const d=new Date();d.setDate(d.getDate()-n);return d.toISOString().slice(0,10)};
const number=(v)=>Number(v||0);

export default function ApprovalOverview({session,onSignOut,analytics=false}) {
  const [fromDate,setFromDate]=useState(daysAgo(89));const [toDate,setToDate]=useState(today());const [data,setData]=useState(null);const [loading,setLoading]=useState(true);const [error,setError]=useState("");
  const load=useCallback(async()=>{setLoading(true);setError("");try{const q=new URLSearchParams({fromDate,toDate});setData(await api.get(`/approver/dashboard?${q}`));}catch(e){setError(e.message||"Unable to load plant dashboard.");}finally{setLoading(false);}},[fromDate,toDate]);
  // eslint-disable-next-line react-hooks/set-state-in-effect
  useEffect(()=>{void load();},[load]);
  const summary=data?.summary||{};const trend=data?.trend||[];const peak=Math.max(1,...trend.flatMap(x=>[number(x.target),number(x.actual)]));
  const kpis=[[FileText,"Total reports",summary.totalReports,""],[Clock3,"Pending approvals",summary.pending,"pending"],[CheckCircle2,"Approved",summary.approved,"approved"],[XCircle,"Correction requested",summary.rejected,"rejected"]];
  return <ApproverShell user={session.user} onSignOut={onSignOut}>
    <header className="approver-page-heading"><div><span>{analytics?"PRODUCTION ANALYTICS":"PLANT PERFORMANCE"}</span><h1>{analytics?"Plant analytics":"Approver overview"}</h1><p>Live reports and approved production for your assigned customers.</p></div><button className="approver-action secondary" onClick={load} disabled={loading}><RefreshCw size={14}/>{loading?"Refreshing…":"Refresh"}</button></header>
    {error&&<div className="approver-error" role="alert">{error}</div>}
    <div className="approver-toolbar"><label>From<input type="date" value={fromDate} max={toDate} onChange={e=>setFromDate(e.target.value)}/></label><label>To<input type="date" value={toDate} min={fromDate} max={today()} onChange={e=>setToDate(e.target.value)}/></label><button className="approver-action" onClick={load} disabled={loading}>Apply dates</button></div>
    <section className="approver-kpis">{kpis.map(([Icon,label,value,tone])=><div className={`approver-kpi ${tone}`} key={label}><span><Icon size={15}/> {label}</span><strong>{loading?"—":number(value).toLocaleString("en-IN")}</strong></div>)}</section>
    <section className="approver-dashboard-grid"><article className="approver-panel approver-chart"><h2>Approved production trend</h2>{!trend.length?<div className="approver-empty"><Activity size={20}/><strong>No approved production in this date range</strong><span>Approved reports will appear here.</span></div>:<><div className="approver-trend" aria-label="Daily production actual and target">{trend.map(point=><div className="approver-trend-item" key={point.date}><div className="approver-trend-bars" title={`${point.date}: actual ${number(point.actual)}, target ${number(point.target)}`}><i className="target" style={{height:`${Math.max(2,number(point.target)/peak*100)}%`}}/><i style={{height:`${Math.max(2,number(point.actual)/peak*100)}%`}}/></div><small>{new Date(`${point.date}T00:00:00`).toLocaleDateString("en-IN",{day:"numeric",month:"short"})}</small></div>)}</div><div className="approver-legend"><span><b/> Actual</span><span><b className="target"/> Target</span></div></>}</article>
      <article className="approver-panel approver-chart"><h2>FATP / SMT performance</h2>{["fatp","smt"].map(key=>{const item=data?.[key]||{reports:0,target:0,actual:0};const pct=number(item.target)?Math.min(100,number(item.actual)/number(item.target)*100):0;return <div className="approver-category-card" key={key}><strong>{key.toUpperCase()}</strong><p>{number(item.reports)} approved reports · {number(item.actual).toLocaleString("en-IN")} / {number(item.target).toLocaleString("en-IN")} production target</p><div className="approver-progress"><i style={{width:`${pct}%`}}/></div></div>})}<div className="approver-quick-links"><Link to="/approvals/reports">Browse daily reports</Link><Link to="/approvals">Review pending reports</Link></div></article>
    </section>
  </ApproverShell>;
}
