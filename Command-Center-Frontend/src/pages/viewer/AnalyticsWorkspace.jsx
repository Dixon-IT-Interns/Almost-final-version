import { useCallback, useEffect, useMemo, useState } from "react";
import {
  Activity, ArrowDownRight, ArrowUpRight, BarChart3, BookmarkPlus,
  Boxes, Check, CircleAlert, Download, Factory, FilterX, Gauge,
  LineChart as LineChartIcon, LoaderCircle, RefreshCw, Save, Search,
  Settings2, Sparkles, Target, Trash2, TrendingUp, Users,
} from "lucide-react";
import {
  Area, AreaChart, Bar, BarChart, CartesianGrid, Cell, ComposedChart,
  Legend, Line, LineChart, Pie, PieChart, ResponsiveContainer, Scatter,
  ScatterChart, Tooltip, XAxis, YAxis,
} from "recharts";
import { api } from "../../api";
import "./AnalyticsWorkspace.css";

const palette = ["#203a68", "#5979c7", "#24865b", "#e88724", "#a24a63", "#6a56a5"];
const metrics = [
  ["productionActual", "Production Actual"],
  ["productionTarget", "Production Target"],
  ["achievement", "Target Achievement %"],
  ["fpyActual", "FPY Actual"],
  ["fpyTarget", "FPY Target"],
  ["ftyActual", "FTY Actual"],
  ["ftyTarget", "FTY Target"],
  ["rtyActual", "RTY Actual"],
  ["rtyTarget", "RTY Target"],
];
const dimensions = [
  ["date", "Reporting Date"],
  ["period", "Reporting Period"],
  ["plant", "Plant"],
  ["customer", "Customer"],
  ["category", "Category"],
  ["line", "Production Line"],
  ["model", "Model"],
];
const presets = ["Today", "Yesterday", "Last 7 Days", "Last 30 Days", "Last 90 Days", "This Week", "Previous Week", "This Month", "Previous Month", "This Quarter", "Custom Range"];
const chartTypes = [
  ["line", "Line Chart"], ["bar", "Bar Chart"], ["grouped", "Grouped Bar Chart"],
  ["stacked", "Stacked Bar Chart"], ["area", "Area Chart"], ["pie", "Pie Chart"],
  ["donut", "Donut Chart"], ["scatter", "Scatter Chart"],
];
const emptyFilters = { customerIds: [], categoryIds: [], lineIds: [], modelIds: [] };
const localDate = date => `${date.getFullYear()}-${String(date.getMonth() + 1).padStart(2, "0")}-${String(date.getDate()).padStart(2, "0")}`;
const addDays = (date, days) => {
  const result = new Date(date);
  result.setDate(result.getDate() + days);
  return result;
};
const queryWith = (params, filters) => {
  for (const [key, values] of Object.entries(filters)) {
    for (const value of values || []) params.append(key, value);
  }
  return params.toString();
};
const number = value => value == null || value === "" ? null : Number(value);
const formatNumber = value => Number(value || 0).toLocaleString("en-IN", { maximumFractionDigits: 2 });
const percentChange = (current, previous) => {
  if (!previous) return null;
  return ((current - previous) / Math.abs(previous)) * 100;
};
const metricsLabel = key => metrics.find(([value]) => value === key)?.[1] || key;
const dimensionLabel = key => dimensions.find(([value]) => value === key)?.[1] || key;
const optionId = option => option.id ?? option.customerId ?? option.categoryId ?? option.lineId ?? option.modelId;
const optionName = option => option.name ?? option.customerName ?? option.categoryName ?? option.lineName ?? option.modelName;

function getPresetRange(preset) {
  const today = new Date();
  const end = new Date(today.getFullYear(), today.getMonth(), today.getDate());
  let start = new Date(end);
  let rangeEnd = new Date(end);
  if (preset === "Yesterday") {
    start = addDays(end, -1);
    rangeEnd = new Date(start);
  } else if (preset.startsWith("Last ")) {
    const days = Number(preset.match(/\d+/)?.[0] || 7);
    start = addDays(end, -(days - 1));
  } else if (preset === "This Week" || preset === "Previous Week") {
    const mondayOffset = (end.getDay() + 6) % 7;
    start = addDays(end, -mondayOffset);
    if (preset === "Previous Week") {
      start = addDays(start, -7);
      rangeEnd = addDays(start, 6);
    }
  } else if (preset === "This Month") {
    start = new Date(end.getFullYear(), end.getMonth(), 1);
  } else if (preset === "Previous Month") {
    start = new Date(end.getFullYear(), end.getMonth() - 1, 1);
    rangeEnd = new Date(end.getFullYear(), end.getMonth(), 0);
  } else if (preset === "This Quarter") {
    start = new Date(end.getFullYear(), Math.floor(end.getMonth() / 3) * 3, 1);
  }
  return { startDate: localDate(start), endDate: localDate(rangeEnd) };
}

export default function AnalyticsWorkspace({ session, onOpenDailyReport }) {
  const [section, setSection] = useState("overview");
  const today = localDate(new Date());
  const [filters, setFilters] = useState({
    plantId: "", startDate: localDate(addDays(new Date(), -29)), endDate: today,
    process: "ALL", ...emptyFilters,
  });
  const [preset, setPreset] = useState("Last 30 Days");
  const [plants, setPlants] = useState([]);
  const [options, setOptions] = useState({ customers: [], categories: [], lines: [], models: [] });
  const [overview, setOverview] = useState(null);
  const [loading, setLoading] = useState(true);
  const [optionsLoading, setOptionsLoading] = useState(false);
  const [error, setError] = useState("");
  const [chartType, setChartType] = useState("grouped");
  const [dimension, setDimension] = useState("date");
  const [groupBy, setGroupBy] = useState("");
  const [aggregation, setAggregation] = useState("SUM");
  const [selectedMetrics, setSelectedMetrics] = useState(["productionActual", "productionTarget"]);
  const [chartRows, setChartRows] = useState([]);
  const [chartLoading, setChartLoading] = useState(false);
  const [explorerData, setExplorerData] = useState(null);
  const [explorerLoading, setExplorerLoading] = useState(false);
  const [explorerPage, setExplorerPage] = useState(1);
  const [explorerPageSize, setExplorerPageSize] = useState(25);
  const [explorerSearch, setExplorerSearch] = useState("");
  const [sortBy, setSortBy] = useState("date");
  const [sortDirection, setSortDirection] = useState("DESC");
  const [savedViews, setSavedViews] = useState([]);
  const [savedLoading, setSavedLoading] = useState(false);
  const [viewName, setViewName] = useState("");
  const [renaming, setRenaming] = useState({});
  const [saving, setSaving] = useState(false);
  const [trendPeriod, setTrendPeriod] = useState("day");
  const [search, setSearch] = useState("");
  const assignedPlantId = session?.user?.plantId;

  const setFilter = (key, value) => setFilters(current => ({ ...current, [key]: value }));
  const queryFilters = useMemo(() => ({
    customerIds: filters.customerIds,
    categoryIds: filters.categoryIds,
    lineIds: filters.lineIds,
    modelIds: filters.modelIds,
  }), [filters.customerIds, filters.categoryIds, filters.lineIds, filters.modelIds]);

  useEffect(() => {
    let active = true;
    api.get("/viewer/plants").then(rows => {
      if (!active) return;
      const availablePlants = Array.isArray(rows) ? rows : [];
      setPlants(availablePlants);
      const assigned = availablePlants.find(plant => String(plant.plantId) === String(assignedPlantId));
      setFilters(current => ({ ...current, plantId: String(assigned?.plantId ?? availablePlants[0]?.plantId ?? "") }));
    }).catch(exception => active && setError(exception.message || "Unable to load assigned plants."));
    return () => { active = false; };
  }, [assignedPlantId]);

  useEffect(() => {
    if (!filters.plantId) return undefined;
    let active = true;
    const controller = new AbortController();
    const timer = window.setTimeout(() => {
      setOptionsLoading(true);
      const params = new URLSearchParams({ plantId: filters.plantId, process: filters.process });
      for (const customerId of filters.customerIds) params.append("customerIds", customerId);
      for (const categoryId of filters.categoryIds) params.append("categoryIds", categoryId);
      for (const lineId of filters.lineIds) params.append("lineIds", lineId);
      api.get(`/viewer/analytics/filters?${params}`, { signal: controller.signal }).then(result => {
        if (!active) return;
        setOptions({
          customers: result.customers || [],
          categories: result.categories || [],
          lines: result.lines || [],
          models: result.models || [],
        });
      }).catch(exception => active && exception.name !== "AbortError" && setError(exception.message || "Unable to load filter options."))
        .finally(() => active && setOptionsLoading(false));
    }, 0);
    return () => { active = false; controller.abort(); window.clearTimeout(timer); };
  }, [filters.plantId, filters.process, filters.customerIds, filters.categoryIds, filters.lineIds]);

  const loadOverview = useCallback(async (signal) => {
    if (!filters.plantId || !filters.startDate || !filters.endDate || filters.endDate < filters.startDate) {
      setOverview(null);
      setLoading(false);
      return;
    }
    setLoading(true);
    setOverview(null);
    setError("");
    try {
      const params = new URLSearchParams({
        plantId: filters.plantId, startDate: filters.startDate, endDate: filters.endDate, process: filters.process,
      });
      const response = await api.get(`/viewer/analytics/overview?${queryWith(params, queryFilters)}`, { signal });
      setOverview(response);
    } catch (exception) {
      if (exception.name === "AbortError") return;
      setOverview(null);
      setError(exception.message || "Unable to load analytics.");
    } finally {
      if (!signal?.aborted) setLoading(false);
    }
  }, [filters.plantId, filters.startDate, filters.endDate, filters.process, queryFilters]);

  useEffect(() => {
    const controller = new AbortController();
    const timer = window.setTimeout(() => loadOverview(controller.signal), 250);
    return () => { window.clearTimeout(timer); controller.abort(); };
  }, [loadOverview]);

  const loadChartData = useCallback(async (signal) => {
    if (!filters.plantId || !filters.startDate || !filters.endDate || !selectedMetrics.length) return;
    setChartLoading(true);
    setError("");
    try {
      const params = new URLSearchParams({
        plantId: filters.plantId,
        startDate: filters.startDate,
        endDate: filters.endDate,
        process: filters.process,
        dimension,
        aggregation,
      });
      if (groupBy) params.set("groupBy", groupBy);
      for (const metric of selectedMetrics) params.append("metrics", metric);
      const rows = await api.get(`/viewer/analytics/chart-data?${queryWith(params, queryFilters)}`, { signal });
      setChartRows(Array.isArray(rows) ? rows : []);
    } catch (exception) {
      if (exception.name === "AbortError") return;
      setChartRows([]);
      setError(exception.message || "Unable to generate chart data.");
    } finally {
      if (!signal?.aborted) setChartLoading(false);
    }
  }, [filters.plantId, filters.startDate, filters.endDate, filters.process, dimension, groupBy, aggregation, selectedMetrics, queryFilters]);

  const loadExplorerData = useCallback(async (signal) => {
    if (!filters.plantId || !filters.startDate || !filters.endDate || filters.endDate < filters.startDate) {
      setExplorerData(null);
      setExplorerLoading(false);
      return;
    }
    setExplorerLoading(true);
    setError("");
    try {
      const params = new URLSearchParams({
        plantId: filters.plantId,
        startDate: filters.startDate,
        endDate: filters.endDate,
        process: filters.process,
        search: explorerSearch,
        sortBy,
        sortDirection,
        page: String(explorerPage),
        pageSize: String(explorerPageSize),
      });
      const response = await api.get(`/viewer/analytics/data?${queryWith(params, queryFilters)}`, { signal });
      setExplorerData(response);
      const lastPage = Math.max(1, Math.ceil(response.summary.totalRows / explorerPageSize));
      if (explorerPage > lastPage) setExplorerPage(lastPage);
    } catch (exception) {
      if (exception.name === "AbortError") return;
      setExplorerData(null);
      setError(exception.message || "Unable to load filtered report data.");
    } finally {
      if (!signal?.aborted) setExplorerLoading(false);
    }
  }, [filters.plantId, filters.startDate, filters.endDate, filters.process, queryFilters, explorerSearch, sortBy, sortDirection, explorerPage, explorerPageSize]);

  useEffect(() => {
    if (section !== "builder") return undefined;
    const controller = new AbortController();
    const timer = window.setTimeout(() => loadChartData(controller.signal), 250);
    return () => { window.clearTimeout(timer); controller.abort(); };
  }, [section, loadChartData]);

  useEffect(() => {
    if (section !== "explorer") return undefined;
    const controller = new AbortController();
    const timer = window.setTimeout(() => loadExplorerData(controller.signal), 250);
    return () => { window.clearTimeout(timer); controller.abort(); };
  }, [section, loadExplorerData]);

  const loadSavedViews = useCallback(async () => {
    setSavedLoading(true);
    try {
      const result = await api.get("/viewer/analytics/saved-views");
      setSavedViews(Array.isArray(result) ? result : []);
    } catch (exception) {
      setError(exception.message || "Unable to load saved analytics views.");
    } finally {
      setSavedLoading(false);
    }
  }, []);

  useEffect(() => {
    if (section !== "saved") return undefined;
    const timer = window.setTimeout(() => { void loadSavedViews(); }, 0);
    return () => window.clearTimeout(timer);
  }, [section, loadSavedViews]);

  const filteredLines = useMemo(() => {
    const lines = [...(overview?.topLines || []), ...(overview?.bottomLines || [])];
    const unique = [...new Map(lines.map(line => [line.id, line])).values()];
    const query = search.trim().toLowerCase();
    return unique.filter(line => !query || line.name.toLowerCase().includes(query));
  }, [overview?.topLines, overview?.bottomLines, search]);

  const aggregatedTrend = useMemo(() => groupTrend(overview?.trend || [], trendPeriod), [overview?.trend, trendPeriod]);
  const builderRows = useMemo(() => normalizeChartRows(chartRows, selectedMetrics), [chartRows, selectedMetrics]);
  const insights = useMemo(() => makeInsights(overview), [overview]);

  function applyPreset(nextPreset) {
    setPreset(nextPreset);
    if (nextPreset !== "Custom Range") {
      const range = getPresetRange(nextPreset);
      setFilters(current => ({ ...current, ...range }));
    }
  }

  function clearFilters() {
    const range = getPresetRange("Last 30 Days");
    setPreset("Last 30 Days");
    setFilters(current => ({ plantId: current.plantId, ...range, process: "ALL", ...emptyFilters }));
  }

  function updateSelection(key, event) {
    const values = Array.from(event.target.selectedOptions, option => option.value);
    setFilters(current => ({ ...current, [key]: values }));
  }

  function applySavedView(view) {
    try {
      const config = JSON.parse(view.configurationJson);
      const savedFilters = config.filters || {};
      setFilters(current => ({
        ...current,
        ...savedFilters,
        plantId: String(savedFilters.plantId || current.plantId),
        customerIds: (savedFilters.customerIds || []).map(String),
        categoryIds: (savedFilters.categoryIds || []).map(String),
        lineIds: (savedFilters.lineIds || []).map(String),
        modelIds: (savedFilters.modelIds || []).map(String),
      }));
      if (config.chart) {
        setChartType(config.chart.chartType || "grouped");
        setDimension(config.chart.dimension || "date");
        setGroupBy(config.chart.groupBy || "");
        setAggregation(config.chart.aggregation || "SUM");
        setSelectedMetrics(config.chart.metrics || ["productionActual"]);
        setSection("builder");
      } else {
        setSection("overview");
      }
    } catch {
      setError("This saved view has an invalid configuration and could not be opened.");
    }
  }

  async function saveView(name = viewName, config = currentConfiguration(filters, chartType, dimension, groupBy, aggregation, selectedMetrics)) {
    const trimmed = name.trim();
    if (!trimmed) {
      setError("Enter a name before saving this view.");
      return null;
    }
    setSaving(true);
    setError("");
    try {
      const created = await api.post("/viewer/analytics/saved-views", {
        name: trimmed,
        configurationJson: JSON.stringify(config),
      });
      setSavedViews(current => [created, ...current]);
      setViewName("");
      return created;
    } catch (exception) {
      setError(exception.message || "Unable to save this view. Confirm the saved-view database migration is applied.");
      return null;
    } finally {
      setSaving(false);
    }
  }

  async function renameView(view) {
    const name = (renaming[view.id] || "").trim();
    if (!name) return;
    setSaving(true);
    try {
      await api.put(`/viewer/analytics/saved-views/${view.id}`, {
        name, configurationJson: view.configurationJson,
      });
      setRenaming(current => ({ ...current, [view.id]: "" }));
      await loadSavedViews();
    } catch (exception) {
      setError(exception.message || "Unable to rename this saved view.");
    } finally {
      setSaving(false);
    }
  }

  async function deleteView(view) {
    setSaving(true);
    try {
      await api.delete(`/viewer/analytics/saved-views/${view.id}`);
      setSavedViews(current => current.filter(item => item.id !== view.id));
    } catch (exception) {
      setError(exception.message || "Unable to delete this saved view.");
    } finally {
      setSaving(false);
    }
  }

  function exportChartData() {
    if (!builderRows.length) return;
    const keys = ["label", ...(groupBy ? ["series"] : []), ...selectedMetrics];
    const quote = value => `"${String(value ?? "").replaceAll("\"", "\"\"")}"`;
    const csv = [keys.map(quote).join(","), ...builderRows.map(row => keys.map(key => quote(row[key])).join(","))].join("\r\n");
    const link = document.createElement("a");
    link.href = URL.createObjectURL(new Blob([csv], { type: "text/csv;charset=utf-8" }));
    link.download = `dixon-analytics-${filters.startDate}-${filters.endDate}.csv`;
    link.click();
    URL.revokeObjectURL(link.href);
  }

  const activeFilters = filters.customerIds.length + filters.categoryIds.length + filters.lineIds.length + filters.modelIds.length;

  return <div className="viewer-page">
    <main className="viewer-main analytics-workspace">
      <header className="analytics-heading">
        <div>
          <span className="eyebrow">MANUFACTURING INTELLIGENCE</span>
          <h1>Analytics Workspace</h1>
          <p>Production, quality and line performance across your authorized plant scope.</p>
        </div>
        <div className="viewer-actions">
          <button className="secondary-btn" onClick={onOpenDailyReport}><Factory size={16}/> Daily work progress</button>
          <button className="secondary-btn" onClick={() => loadOverview()} disabled={loading}><RefreshCw size={16}/> Refresh</button>
        </div>
      </header>

      <nav className="analytics-tabs" aria-label="Analytics sections">
        {[["overview", "Overview"], ["explorer", "Data Explorer"], ["builder", "Chart Builder"], ["insights", "Insights"], ["saved", "Saved Views"]].map(([key, label]) =>
          <button type="button" key={key} className={section === key ? "is-active" : ""} onClick={() => setSection(key)}>{label}</button>)}
      </nav>

      <section className="analytics-filters" aria-label="Analytics filters">
        <div className="analytics-filter-grid">
          <label>Plant<select value={filters.plantId} onChange={event => {
            setFilters(current => ({ ...current, plantId: event.target.value, ...emptyFilters }));
          }}><option value="">Select plant</option>{plants.map(plant => <option key={plant.plantId} value={plant.plantId}>{plant.plantName}</option>)}</select></label>
          <label>Start date<input type="date" value={filters.startDate} max={filters.endDate} onChange={event => { setPreset("Custom Range"); setFilter("startDate", event.target.value); }}/></label>
          <label>End date<input type="date" value={filters.endDate} min={filters.startDate} onChange={event => { setPreset("Custom Range"); setFilter("endDate", event.target.value); }}/></label>
          <label>Category<select value={filters.process} onChange={event => { setFilters(current => ({ ...current, process: event.target.value, categoryIds: [], lineIds: [], modelIds: [] })); }}><option value="ALL">SMT + FATP</option><option value="SMT">SMT</option><option value="FATP">FATP</option></select></label>
          <MultiSelect label="Customer / brand" values={filters.customerIds} options={options.customers} onChange={event => {
            const selected = Array.from(event.target.selectedOptions, option => option.value);
            setFilters(current => ({ ...current, customerIds: selected, categoryIds: [], lineIds: [], modelIds: [] }));
          }} />
          <MultiSelect label="Category" values={filters.categoryIds} options={options.categories} onChange={event => {
            const selected = Array.from(event.target.selectedOptions, option => option.value);
            setFilters(current => ({ ...current, categoryIds: selected, lineIds: [], modelIds: [] }));
          }} />
          <MultiSelect label="Production line" values={filters.lineIds} options={options.lines} onChange={event => {
            const selected = Array.from(event.target.selectedOptions, option => option.value);
            setFilters(current => ({ ...current, lineIds: selected, modelIds: [] }));
          }} />
          <MultiSelect label="Model" values={filters.modelIds} options={options.models} onChange={event => updateSelection("modelIds", event)} />
        </div>
        <div className="analytics-filter-footer">
          <div className="analytics-presets">
            {presets.map(item => <button key={item} type="button" className={preset === item ? "selected" : ""} onClick={() => applyPreset(item)}>{item}</button>)}
          </div>
          <div className="analytics-filter-status">
            {optionsLoading && <span><LoaderCircle size={13} className="analytics-spin"/> Updating linked filters</span>}
            <span>{activeFilters ? `${activeFilters} dimensions selected` : "All accessible dimensions"}</span>
            <button type="button" onClick={clearFilters}><FilterX size={14}/> Clear filters</button>
          </div>
        </div>
      </section>

      {error && <div className="viewer-error" role="alert">{error}</div>}
      {loading && !overview && <div className="analytics-loading"><LoaderCircle size={22} className="analytics-spin"/> Loading filtered production data…</div>}
      {!loading && !error && !overview && <div className="analytics-empty"><CircleAlert size={25}/><strong>No data is available for this range.</strong><span>Adjust the date range or filters and try again.</span></div>}

      {section === "overview" && overview && <OverviewSection
        overview={overview} trend={aggregatedTrend} trendPeriod={trendPeriod} setTrendPeriod={setTrendPeriod}
        search={search} setSearch={setSearch} filteredLines={filteredLines} onSave={() => saveView(viewName || `Overview ${filters.startDate}`)}
        saving={saving} onOpenExplorer={() => setSection("builder")} />
      }
      {section === "explorer" && <DataExplorer
        data={explorerData} loading={explorerLoading} page={explorerPage} pageSize={explorerPageSize}
        search={explorerSearch} sortBy={sortBy} sortDirection={sortDirection}
        onSearch={value => { setExplorerSearch(value); setExplorerPage(1); }}
        onPage={setExplorerPage}
        onPageSize={value => { setExplorerPageSize(value); setExplorerPage(1); }}
        onSort={key => {
          if (sortBy === key) setSortDirection(value => value === "ASC" ? "DESC" : "ASC");
          else { setSortBy(key); setSortDirection(key === "date" ? "DESC" : "ASC"); }
          setExplorerPage(1);
        }}
        onExport={async () => {
          try {
            const params = new URLSearchParams({
              plantId: filters.plantId,
              startDate: filters.startDate,
              endDate: filters.endDate,
              process: filters.process,
              search: explorerSearch,
              sortBy,
              sortDirection,
            });
            const blob = await api.download(`/viewer/analytics/export?${queryWith(params, queryFilters)}`);
            const url = URL.createObjectURL(blob);
            const link = document.createElement("a");
            link.href = url;
            link.download = `Dixon_Analytics_${filters.startDate}_${filters.endDate}.xlsx`;
            link.click();
            URL.revokeObjectURL(url);
          } catch (exception) {
            setError(exception.message || "Unable to export filtered data.");
          }
        }}
      />}
      {section === "builder" && <ChartBuilder
        chartType={chartType} setChartType={setChartType} dimension={dimension} setDimension={setDimension}
        groupBy={groupBy} setGroupBy={setGroupBy} aggregation={aggregation} setAggregation={setAggregation}
        selectedMetrics={selectedMetrics} setSelectedMetrics={setSelectedMetrics} data={builderRows}
        loading={chartLoading} onGenerate={() => loadChartData()} onExport={exportChartData}
        viewName={viewName} setViewName={setViewName} onSave={saveView} saving={saving}
      />}
      {section === "insights" && <InsightsSection insights={insights} overview={overview} />}
      {section === "saved" && <SavedViewsSection
        views={savedViews} loading={savedLoading} saving={saving} renaming={renaming} setRenaming={setRenaming}
        onOpen={applySavedView} onRename={renameView} onDelete={deleteView} onRefresh={loadSavedViews}
        viewName={viewName} setViewName={setViewName} onSave={saveView}
      />}
    </main>
  </div>;
}

function MultiSelect({ label, values, options, onChange }) {
  return <label>{label}<select multiple value={values} onChange={onChange} aria-label={label} size="1">
    {options.map(option => <option key={optionId(option)} value={optionId(option)}>{optionName(option)}</option>)}
  </select><small>Use Ctrl / Cmd to select multiple</small></label>;
}

function OverviewSection({ overview, trend, trendPeriod, setTrendPeriod, search, setSearch, filteredLines, onSave, saving, onOpenExplorer }) {
  const current = overview.current;
  const previous = overview.previous;
  const cards = [
    { title: "Production actual", value: current.productionActual, previous: previous.productionActual, icon: <Boxes size={17}/>, kind: "number" },
    { title: "Production target", value: current.productionTarget, previous: previous.productionTarget, icon: <Target size={17}/>, kind: "number" },
    { title: "Target achievement", value: current.achievement, previous: previous.achievement, icon: <Gauge size={17}/>, kind: "percent", points: true },
    { title: "Average FPY", value: current.fpy, previous: previous.fpy, icon: <Activity size={17}/>, kind: "percent", points: true },
    { title: "Average FTY", value: current.fty, previous: previous.fty, icon: <TrendingUp size={17}/>, kind: "percent", points: true },
    { title: "Average RTY", value: current.rty, previous: previous.rty, icon: <LineChartIcon size={17}/>, kind: "percent", points: true },
    { title: "Active production lines", value: current.activeLines, previous: previous.activeLines, icon: <Factory size={17}/>, kind: "count" },
    { title: "Models reported", value: current.modelsReported, previous: previous.modelsReported, icon: <Users size={17}/>, kind: "count" },
  ];
  return <>
    <section className="analytics-kpi-grid">
      {cards.map(card => <KpiCard key={card.title} {...card} />)}
    </section>
    <div className="analytics-chart-grid">
      <Panel title="Production actual vs target" subtitle="Daily production across the selected range" className="analytics-chart-large">
        <ChartFrame data={overview.trend} empty="No production records for this range.">
          <ResponsiveContainer width="100%" height="100%"><BarChart data={overview.trend} margin={{ top: 8, right: 12, bottom: 4, left: 0 }}>
            <CartesianGrid strokeDasharray="3 3" vertical={false} stroke="#e8ebf1"/>
            <XAxis dataKey="date" tickFormatter={shortDate} minTickGap={24}/>
            <YAxis width={52} tickFormatter={compactNumber}/>
            <Tooltip content={<AnalyticsTooltip/>}/><Legend/>
            <Bar dataKey="productionActual" name="Actual" fill={palette[0]} radius={[4, 4, 0, 0]}/>
            <Bar dataKey="productionTarget" name="Target" fill={palette[1]} radius={[4, 4, 0, 0]}/>
          </BarChart></ResponsiveContainer>
        </ChartFrame>
      </Panel>
      <Panel title="FPY, FTY & RTY trend" subtitle="Daily average quality performance">
        <ChartFrame data={overview.trend.filter(row => row.fpy != null || row.fty != null || row.rty != null)} empty="No quality KPI values are available.">
          <ResponsiveContainer width="100%" height="100%"><LineChart data={overview.trend} margin={{ top: 8, right: 10, bottom: 4, left: 0 }}>
            <CartesianGrid strokeDasharray="3 3" vertical={false} stroke="#e8ebf1"/>
            <XAxis dataKey="date" tickFormatter={shortDate} minTickGap={24}/>
            <YAxis width={45}/><Tooltip content={<AnalyticsTooltip suffix="%"/>}/><Legend/>
            <Line type="monotone" dataKey="fpy" name="FPY" stroke={palette[0]} strokeWidth={2.5} dot={false} connectNulls/>
            <Line type="monotone" dataKey="fty" name="FTY" stroke={palette[2]} strokeWidth={2.5} dot={false} connectNulls/>
            <Line type="monotone" dataKey="rty" name="RTY" stroke={palette[3]} strokeWidth={2.5} dot={false} connectNulls/>
          </LineChart></ResponsiveContainer>
        </ChartFrame>
      </Panel>
      <Panel title="Customer production contribution" subtitle="Actual production by customer">
        <ChartFrame data={overview.customers} empty="No customer production data is available.">
          <ResponsiveContainer width="100%" height="100%"><PieChart>
            <Pie data={overview.customers} dataKey="productionActual" nameKey="name" innerRadius="54%" outerRadius="78%" paddingAngle={3}>
              {overview.customers.map((item, index) => <Cell key={item.id} fill={palette[index % palette.length]}/>)}
            </Pie>
            <Tooltip content={<AnalyticsTooltip/>}/><Legend verticalAlign="bottom" height={32}/>
          </PieChart></ResponsiveContainer>
        </ChartFrame>
      </Panel>
      <Panel title="SMT vs FATP performance" subtitle="Production actual, target and achievement">
        <ChartFrame data={overview.categories} empty="No category data is available.">
          <ResponsiveContainer width="100%" height="100%"><ComposedChart data={overview.categories} margin={{ top: 8, right: 12, bottom: 4, left: 0 }}>
            <CartesianGrid strokeDasharray="3 3" vertical={false} stroke="#e8ebf1"/>
            <XAxis dataKey="name"/><YAxis yAxisId="production" width={52} tickFormatter={compactNumber}/><YAxis yAxisId="achievement" orientation="right" width={42} unit="%"/>
            <Tooltip content={<AnalyticsTooltip/>}/><Legend/>
            <Bar yAxisId="production" dataKey="productionActual" name="Actual" fill={palette[0]} radius={[4, 4, 0, 0]}/>
            <Bar yAxisId="production" dataKey="productionTarget" name="Target" fill={palette[1]} radius={[4, 4, 0, 0]}/>
            <Line yAxisId="achievement" dataKey="achievement" name="Achievement %" stroke={palette[3]} strokeWidth={2} dot/>
          </ComposedChart></ResponsiveContainer>
        </ChartFrame>
      </Panel>
      <Panel title="Daily / weekly / monthly trend" subtitle="Change the reporting granularity" className="analytics-chart-large">
        <div className="analytics-chart-switch">
          {["day", "week", "month"].map(period => <button key={period} className={trendPeriod === period ? "selected" : ""} onClick={() => setTrendPeriod(period)}>{period}</button>)}
        </div>
        <ChartFrame data={trend} empty="No production records for this range.">
          <ResponsiveContainer width="100%" height="100%"><AreaChart data={trend} margin={{ top: 8, right: 12, bottom: 4, left: 0 }}>
            <CartesianGrid strokeDasharray="3 3" vertical={false} stroke="#e8ebf1"/>
            <XAxis dataKey="label" minTickGap={24}/><YAxis width={52} tickFormatter={compactNumber}/>
            <Tooltip content={<AnalyticsTooltip/>}/><Legend/>
            <Area type="monotone" dataKey="productionActual" name="Actual" stroke={palette[0]} fill="#203a6820" strokeWidth={2.5}/>
            <Area type="monotone" dataKey="productionTarget" name="Target" stroke={palette[1]} fill="#5979c720" strokeWidth={2}/>
          </AreaChart></ResponsiveContainer>
        </ChartFrame>
      </Panel>
      <Panel title="Target achievement overview" subtitle="Line achievement against selected targets">
        <ChartFrame data={[...(overview.topLines || []), ...(overview.bottomLines || [])]} empty="No target values are available.">
          <ResponsiveContainer width="100%" height="100%"><BarChart data={[...(overview.topLines || []), ...(overview.bottomLines || [])]} layout="vertical" margin={{ top: 4, right: 22, bottom: 4, left: 16 }}>
            <CartesianGrid strokeDasharray="3 3" horizontal={false} stroke="#e8ebf1"/>
            <XAxis type="number" domain={[0, "dataMax"]} unit="%"/>
            <YAxis type="category" dataKey="name" width={95} tick={{ fontSize: 10 }}/>
            <Tooltip content={<AnalyticsTooltip suffix="%"/>}/>
            <Bar dataKey="achievement" name="Achievement" fill={palette[2]} radius={[0, 4, 4, 0]}/>
          </BarChart></ResponsiveContainer>
        </ChartFrame>
      </Panel>
    </div>
    <section className="analytics-panel analytics-performance">
      <div className="analytics-panel-heading">
        <div><h2>Production line ranking</h2><p>Only lines with a reported production target are ranked.</p></div>
        <label className="analytics-search"><Search size={15}/><input value={search} onChange={event => setSearch(event.target.value)} placeholder="Search a line"/></label>
      </div>
      <div className="analytics-ranking-grid">
        <RankingTable title="Top 5 performing lines" rows={overview.topLines} accent="good" search={search}/>
        <RankingTable title="Bottom 5 performing lines" rows={overview.bottomLines} accent="watch" search={search}/>
      </div>
      <div className="analytics-panel-actions"><button className="secondary-btn" onClick={onOpenExplorer}><Settings2 size={15}/> Explore this data</button><button className="secondary-btn" onClick={onSave} disabled={saving}><BookmarkPlus size={15}/> Save current view</button></div>
      {filteredLines.length === 0 && search && <div className="analytics-empty-inline">No ranked production lines match “{search}”.</div>}
    </section>
  </>;
}

function KpiCard({ title, value, previous, icon, kind, points = false }) {
  const current = number(value);
  const prior = number(previous);
  const delta = points ? (current == null || prior == null ? null : current - prior) : percentChange(current || 0, prior || 0);
  const up = delta != null && delta >= 0;
  const formatted = current == null ? "—" : kind === "percent" ? `${current.toFixed(2)}%` : formatNumber(current);
  const priorFormatted = prior == null ? "—" : kind === "percent" ? `${prior.toFixed(2)}%` : formatNumber(prior);
  return <article className="analytics-kpi-card">
    <div className="analytics-kpi-icon">{icon}</div>
    <div className="analytics-kpi-label">{title}</div>
    <strong>{formatted}</strong>
    <div className="analytics-kpi-compare"><span>Previous equivalent range</span><b>{priorFormatted}</b></div>
    <div className={`analytics-kpi-delta ${delta == null ? "is-neutral" : up ? "is-up" : "is-down"}`}>
      {delta == null ? <span>Insufficient comparison data</span> : <>
        {up ? <ArrowUpRight size={14}/> : <ArrowDownRight size={14}/>}
        <b>{points ? `${Math.abs(delta).toFixed(2)} pp` : `${Math.abs(delta).toFixed(1)}%`}</b>
        <span>{points ? "change" : "vs previous"}</span>
      </>}
    </div>
  </article>;
}

function RankingTable({ title, rows = [], accent, search }) {
  const query = search.trim().toLowerCase();
  const visible = rows.filter(row => !query || row.name.toLowerCase().includes(query));
  return <div className={`analytics-ranking ${accent}`}>
    <h3>{title}</h3>
    {visible.length ? visible.map((line, index) => <div className="analytics-ranking-row" key={line.id}>
      <span className="analytics-rank">{index + 1}</span><div><strong>{line.name}</strong><small>Actual {formatNumber(line.productionActual)} / Target {formatNumber(line.productionTarget)}</small></div>
      <b>{line.achievement.toFixed(1)}%</b>
    </div>) : <div className="analytics-empty-inline">No ranked lines are available.</div>}
  </div>;
}

function DataExplorer({ data, loading, page, pageSize, search, sortBy, sortDirection, onSearch, onPage, onPageSize, onSort, onExport }) {
  const totalRows = data?.summary?.totalRows || 0;
  const pageCount = Math.max(1, Math.ceil(totalRows / pageSize));
  const columns = [
    ["date", "Reporting date", row => row.date],
    ["customer", "Customer", row => row.customer],
    ["category", "Category", row => row.category],
    ["line", "Production line", row => row.productionLine],
    ["model", "Model", row => row.model],
    ["productionActual", "Production actual", row => formatNumber(row.productionActual)],
    ["productionTarget", "Production target", row => formatNumber(row.productionTarget)],
    ["fpy", "FPY", row => row.fpy == null ? "—" : `${formatNumber(row.fpy)}%`],
    ["fty", "FTY", row => row.fty == null ? "—" : `${formatNumber(row.fty)}%`],
    ["rty", "RTY", row => row.rty == null ? "—" : `${formatNumber(row.rty)}%`],
  ];
  return <section className="analytics-panel analytics-data-explorer">
    <div className="analytics-panel-heading">
      <div><h2>Filtered production records</h2><p>Server-filtered report rows with sortable columns and paged results.</p></div>
      <button className="primary-btn" onClick={onExport} disabled={loading || !totalRows}><Download size={15}/> Export filtered Excel</button>
    </div>
    <div className="analytics-explorer-summary">
      <SummaryChip label="Matching records" value={formatNumber(totalRows)}/>
      <SummaryChip label="Production actual" value={formatNumber(data?.summary?.productionActual)}/>
      <SummaryChip label="Production target" value={formatNumber(data?.summary?.productionTarget)}/>
      <SummaryChip label="Average FPY / FTY / RTY" value={[data?.summary?.fpy, data?.summary?.fty, data?.summary?.rty].map(value => value == null ? "—" : `${formatNumber(value)}%`).join(" / ")}/>
    </div>
    <div className="analytics-explorer-tools">
      <label className="analytics-search"><Search size={15}/><input value={search} onChange={event => onSearch(event.target.value)} placeholder="Search date, customer, line, category or model"/></label>
      <label>Rows<select value={pageSize} onChange={event => onPageSize(Number(event.target.value))}><option value={10}>10</option><option value={25}>25</option><option value={50}>50</option><option value={100}>100</option></select></label>
    </div>
    <div className="analytics-table-scroll">
      <table className="analytics-data-table">
        <thead><tr>{columns.map(([key, title]) => <th key={key}><button type="button" onClick={() => onSort(key)}>{title}{sortBy === key ? ` ${sortDirection === "ASC" ? "↑" : "↓"}` : ""}</button></th>)}</tr></thead>
        <tbody>
          {data?.rows?.map((row, index) => <tr key={`${row.date}-${row.productionLine}-${row.model}-${index}`}>
            {columns.map(([key, , value]) => <td key={key}>{value(row)}</td>)}
          </tr>)}
          {!loading && !data?.rows?.length && <tr><td colSpan={columns.length} className="analytics-table-empty">No records match the selected date range and filters.</td></tr>}
        </tbody>
      </table>
      {loading && <div className="analytics-table-loading"><LoaderCircle size={18} className="analytics-spin"/>Updating filtered records…</div>}
    </div>
    <div className="analytics-pagination"><span>{totalRows ? `Showing ${(page - 1) * pageSize + 1}–${Math.min(page * pageSize, totalRows)} of ${formatNumber(totalRows)}` : "No matching records"}</span><div><button className="secondary-btn" onClick={() => onPage(Math.max(1, page - 1))} disabled={page <= 1 || loading}>Previous</button><b>Page {page} of {pageCount}</b><button className="secondary-btn" onClick={() => onPage(Math.min(pageCount, page + 1))} disabled={page >= pageCount || loading}>Next</button></div></div>
  </section>;
}

function SummaryChip({ label, value }) {
  return <div><span>{label}</span><strong>{value}</strong></div>;
}

function ChartBuilder({ chartType, setChartType, dimension, setDimension, groupBy, setGroupBy, aggregation, setAggregation, selectedMetrics, setSelectedMetrics, data, loading, onGenerate, onExport, viewName, setViewName, onSave, saving }) {
  function toggleMetric(metric) {
    setSelectedMetrics(current => current.includes(metric)
      ? current.filter(item => item !== metric)
      : [...current, metric]);
  }
  const chartContent = <BuilderChart type={chartType} data={data} metrics={selectedMetrics} grouped={Boolean(groupBy && groupBy !== dimension)}/>;
  return <section className="analytics-panel analytics-builder">
    <div className="analytics-panel-heading">
      <div><span className="analytics-kicker"><Sparkles size={14}/> CUSTOM CHART BUILDER</span><h2>Build a chart from authorized production data</h2><p>Choose dimensions and measures; data is aggregated by the analytics API.</p></div>
      <button className="secondary-btn" onClick={onGenerate} disabled={loading}><RefreshCw size={15}/> Refresh chart</button>
    </div>
    <div className="analytics-builder-layout">
      <aside className="analytics-builder-controls">
        <label>Chart type<select value={chartType} onChange={event => setChartType(event.target.value)}>{chartTypes.map(([key, label]) => <option key={key} value={key}>{label}</option>)}</select></label>
        <label>X-axis dimension<select value={dimension} onChange={event => setDimension(event.target.value)}>{dimensions.filter(([key]) => key !== "period").map(([key, label]) => <option key={key} value={key}>{label}</option>)}</select></label>
        <label>Group by<select value={groupBy} onChange={event => setGroupBy(event.target.value)}><option value="">No grouping</option>{dimensions.map(([key, label]) => <option key={key} value={key}>{label}</option>)}</select></label>
        <label>Aggregation<select value={aggregation} onChange={event => setAggregation(event.target.value)}>{["SUM", "AVG", "MIN", "MAX", "COUNT"].map(value => <option key={value} value={value}>{({ SUM: "Sum", AVG: "Average", MIN: "Minimum", MAX: "Maximum", COUNT: "Count" })[value]}</option>)}</select></label>
        <fieldset><legend>Y-axis measures</legend>{metrics.map(([key, label]) => <label className="analytics-check-option" key={key}><input type="checkbox" checked={selectedMetrics.includes(key)} onChange={() => toggleMetric(key)}/>{label}</label>)}</fieldset>
        <div className="analytics-builder-actions">
          <button className="primary-btn" onClick={onGenerate} disabled={loading || !selectedMetrics.length}>{loading ? <LoaderCircle size={15} className="analytics-spin"/> : <BarChart3 size={15}/>} Generate chart</button>
          <button className="secondary-btn" onClick={onExport} disabled={!data.length}><Download size={15}/> Export CSV</button>
        </div>
        <div className="analytics-save-inline"><input value={viewName} onChange={event => setViewName(event.target.value)} placeholder="Name this chart view"/><button className="secondary-btn" onClick={() => onSave(viewName)} disabled={saving || !viewName.trim()}><Save size={15}/> Save</button></div>
      </aside>
      <div className="analytics-chart-preview">
        <div className="analytics-preview-title"><div><strong>Chart preview</strong><small>{dimensionLabel(dimension)} · {aggregation.toLowerCase()} · {selectedMetrics.map(metricsLabel).join(", ")}</small></div>{loading && <LoaderCircle size={17} className="analytics-spin"/>}</div>
        {loading ? <div className="analytics-chart-loading"><LoaderCircle size={23} className="analytics-spin"/>Updating preview…</div>
          : data.length ? chartContent : <div className="analytics-empty"><BarChart3 size={25}/><strong>No chart data found</strong><span>Try a different metric, dimension, date range or filter.</span></div>}
      </div>
    </div>
  </section>;
}

function BuilderChart({ type, data, metrics: activeMetrics, grouped }) {
  const colors = Object.fromEntries(activeMetrics.map((metric, index) => [metric, palette[index % palette.length]]));
  const seriesByMetric = grouped ? [...new Set(data.map(row => row.series).filter(Boolean))] : [];
  const dataKey = activeMetrics[0];
  if (type === "pie" || type === "donut") {
    return <ResponsiveContainer width="100%" height="100%"><PieChart><Pie data={data} dataKey={dataKey} nameKey="label" innerRadius={type === "donut" ? "52%" : 0} outerRadius="78%" paddingAngle={type === "donut" ? 3 : 0}>
      {data.map((row, index) => <Cell key={`${row.label}-${index}`} fill={palette[index % palette.length]}/>)}
    </Pie><Tooltip content={<AnalyticsTooltip/>}/><Legend verticalAlign="bottom"/></PieChart></ResponsiveContainer>;
  }
  if (type === "scatter") {
    return <ResponsiveContainer width="100%" height="100%"><ScatterChart><CartesianGrid stroke="#e8ebf1"/><XAxis type="category" dataKey="label" name="Dimension" minTickGap={20}/><YAxis type="number" dataKey={dataKey} name={metricsLabel(dataKey)}/><Tooltip cursor={{ strokeDasharray: "3 3" }} content={<AnalyticsTooltip/>}/><Scatter data={data} fill={colors[dataKey]}/></ScatterChart></ResponsiveContainer>;
  }
  if (type === "line") {
    return <ResponsiveContainer width="100%" height="100%"><LineChart data={data}><CartesianGrid strokeDasharray="3 3" vertical={false} stroke="#e8ebf1"/><XAxis dataKey="label" minTickGap={20}/><YAxis/><Tooltip content={<AnalyticsTooltip/>}/><Legend/>
      {activeMetrics.flatMap((metric, metricIndex) => grouped ? seriesByMetric.map((series, seriesIndex) => <Line key={`${metric}-${series}`} dataKey={seriesKey(metric, series)} name={`${metricsLabel(metric)} · ${series}`} stroke={palette[(metricIndex + seriesIndex) % palette.length]} strokeWidth={2} dot={false}/>) : <Line key={metric} dataKey={metric} name={metricsLabel(metric)} stroke={colors[metric]} strokeWidth={2.5} dot={false}/>)}
    </LineChart></ResponsiveContainer>;
  }
  if (type === "area") {
    return <ResponsiveContainer width="100%" height="100%"><AreaChart data={data}><CartesianGrid strokeDasharray="3 3" vertical={false} stroke="#e8ebf1"/><XAxis dataKey="label" minTickGap={20}/><YAxis/><Tooltip content={<AnalyticsTooltip/>}/><Legend/>
      {activeMetrics.flatMap((metric, metricIndex) => grouped ? seriesByMetric.map((series, seriesIndex) => <Area key={`${metric}-${series}`} dataKey={seriesKey(metric, series)} name={`${metricsLabel(metric)} · ${series}`} stroke={palette[(metricIndex + seriesIndex) % palette.length]} fill={`${palette[(metricIndex + seriesIndex) % palette.length]}20`} connectNulls/>)
        : <Area key={metric} dataKey={metric} name={metricsLabel(metric)} stroke={colors[metric]} fill={`${colors[metric]}20`} connectNulls/>)}
    </AreaChart></ResponsiveContainer>;
  }
  return <ResponsiveContainer width="100%" height="100%"><BarChart data={data}><CartesianGrid strokeDasharray="3 3" vertical={false} stroke="#e8ebf1"/><XAxis dataKey="label" minTickGap={20}/><YAxis/><Tooltip content={<AnalyticsTooltip/>}/><Legend/>
    {activeMetrics.flatMap((metric, metricIndex) => grouped ? seriesByMetric.map((series, seriesIndex) => <Bar key={`${metric}-${series}`} dataKey={seriesKey(metric, series)} name={`${metricsLabel(metric)} · ${series}`} stackId={type === "stacked" ? metric : undefined} fill={palette[(metricIndex + seriesIndex) % palette.length]}/>)
      : <Bar key={metric} dataKey={metric} name={metricsLabel(metric)} stackId={type === "stacked" ? "stack" : undefined} fill={colors[metric]} radius={[3, 3, 0, 0]}/>)}
  </BarChart></ResponsiveContainer>;
}

function InsightsSection({ insights, overview }) {
  let content;
  if (!overview) {
    content = <div className="analytics-empty"><CircleAlert size={24}/><strong>Analytics are unavailable.</strong><span>Adjust the selected filters and try again.</span></div>;
  } else if (insights.length) {
    content = <div className="analytics-insights-list">
      {insights.map((insight, index) => <article key={index} className={`analytics-insight ${insight.tone}`}>
        <span>{insight.icon}</span><div><strong>{insight.title}</strong><p>{insight.detail}</p></div>
      </article>)}
    </div>;
  } else {
    content = <div className="analytics-empty"><Sparkles size={25}/><strong>No conclusive insights for this range.</strong><span>There is not enough comparison or target data to make a reliable observation.</span></div>;
  }
  return <section className="analytics-panel">
    <div className="analytics-panel-heading"><div><span className="analytics-kicker"><Sparkles size={14}/> RULE-BASED PERFORMANCE INSIGHTS</span><h2>What changed in this reporting range?</h2><p>Insights are computed from the selected, permission-scoped records and previous equivalent range.</p></div></div>
    {content}
  </section>;
}

function SavedViewsSection({ views, loading, saving, renaming, setRenaming, onOpen, onRename, onDelete, onRefresh, viewName, setViewName, onSave }) {
  return <section className="analytics-panel">
    <div className="analytics-panel-heading"><div><span className="analytics-kicker"><BookmarkPlus size={14}/> PERSONAL WORKSPACE</span><h2>Saved analytics views</h2><p>Configurations belong to your account and reopen through current Viewer permissions.</p></div><button className="secondary-btn" onClick={onRefresh} disabled={loading}><RefreshCw size={15}/> Refresh</button></div>
    <div className="analytics-save-bar"><input value={viewName} onChange={event => setViewName(event.target.value)} placeholder="Name the current filter and chart setup"/><button className="primary-btn" onClick={() => onSave(viewName)} disabled={saving || !viewName.trim()}><Save size={15}/> Save current view</button></div>
    {loading ? <div className="analytics-chart-loading"><LoaderCircle size={20} className="analytics-spin"/>Loading saved views…</div>
      : views.length ? <div className="analytics-saved-list">{views.map(view => <article key={view.id} className="analytics-saved-view">
        <div className="analytics-saved-main"><BookmarkPlus size={18}/><div><strong>{view.name}</strong><small>Updated {new Date(view.updatedAt).toLocaleString()}</small></div></div>
        <div className="analytics-saved-actions"><button className="secondary-btn" onClick={() => onOpen(view)}><Check size={14}/> Open</button>
          <div className="analytics-rename"><input value={renaming[view.id] ?? ""} onChange={event => setRenaming(current => ({ ...current, [view.id]: event.target.value }))} placeholder="New name"/><button className="secondary-btn" onClick={() => onRename(view)} disabled={!renaming[view.id]?.trim() || saving}>Rename</button></div>
          <button className="analytics-icon-button" aria-label={`Delete ${view.name}`} onClick={() => onDelete(view)} disabled={saving}><Trash2 size={16}/></button>
        </div>
      </article>)}</div>
      : <div className="analytics-empty"><BookmarkPlus size={25}/><strong>No saved views yet</strong><span>Save a filter and chart configuration to return to it later.</span></div>}
  </section>;
}

function Panel({ title, subtitle, className = "", children }) {
  return <section className={`analytics-panel ${className}`}><div className="analytics-panel-heading"><div><h2>{title}</h2><p>{subtitle}</p></div></div>{children}</section>;
}

function ChartFrame({ data, empty, children }) {
  if (!data?.length) return <div className="analytics-empty-chart">{empty}</div>;
  return <div className="analytics-chart-frame">{children}</div>;
}

function AnalyticsTooltip({ active, payload, label, suffix = "" }) {
  if (!active || !payload?.length) return null;
  return <div className="analytics-tooltip"><strong>{label || payload[0]?.payload?.label || "Production"}</strong>{payload.map((item, index) => {
    const metricSuffix = suffix || (/achievement|fpy|fty|rty/i.test(String(item.dataKey)) ? "%" : "");
    return <div key={`${item.name}-${index}`}><i style={{ background: item.color || item.fill }}/><span>{item.name}</span><b>{formatNumber(item.value)}{metricSuffix}</b></div>;
  })}</div>;
}

function groupTrend(rows, period) {
  if (period === "day") return rows.map(row => ({ ...row, label: shortDate(row.date) }));
  const grouped = new Map();
  for (const row of rows) {
    const date = new Date(`${row.date}T00:00:00`);
    const key = period === "month"
      ? `${date.getFullYear()}-${String(date.getMonth() + 1).padStart(2, "0")}`
      : localDate(addDays(date, -((date.getDay() + 6) % 7)));
    const group = grouped.get(key) || { label: period === "month" ? new Date(`${key}-01T00:00:00`).toLocaleDateString("en-IN", { month: "short", year: "numeric" }) : shortDate(key), productionActual: 0, productionTarget: 0 };
    group.productionActual += Number(row.productionActual || 0);
    group.productionTarget += Number(row.productionTarget || 0);
    grouped.set(key, group);
  }
  return [...grouped.values()];
}

function normalizeChartRows(rows, activeMetrics) {
  const groups = new Map();
  for (const row of rows) {
    const label = String(row.label ?? "Unknown");
    const item = groups.get(label) || { label, index: groups.size };
    if (row.series) {
      for (const metric of activeMetrics) item[seriesKey(metric, row.series)] = number(row[metric]);
      item.series = row.series;
    } else {
      for (const metric of activeMetrics) item[metric] = number(row[metric]);
    }
    groups.set(label, item);
  }
  return [...groups.values()];
}

function seriesKey(metric, series) {
  return `${metric}__${series}`;
}

function makeInsights(overview) {
  if (!overview) return [];
  const { current, previous, previousStartDate, previousEndDate } = overview;
  const insights = [];
  const productionDelta = percentChange(current.productionActual, previous.productionActual);
  const comparisonLabel = `${previousStartDate} to ${previousEndDate}`;
  if (productionDelta != null) {
    const direction = productionDelta >= 0 ? "increased" : "declined";
    insights.push({
      tone: productionDelta >= 0 ? "positive" : "caution",
      icon: productionDelta >= 0 ? <ArrowUpRight size={18}/> : <ArrowDownRight size={18}/>,
      title: `Production ${direction} by ${Math.abs(productionDelta).toFixed(1)}%.`,
      detail: `Actual output is ${formatNumber(current.productionActual)} versus ${formatNumber(previous.productionActual)} in the equivalent prior period (${comparisonLabel}).`,
    });
  }
  for (const [key, label] of [["fpy", "FPY"], ["fty", "FTY"], ["rty", "RTY"]]) {
    if (current[key] == null || previous[key] == null) continue;
    const delta = current[key] - previous[key];
    insights.push({
      tone: delta >= 0 ? "positive" : "caution",
      icon: delta >= 0 ? <ArrowUpRight size={18}/> : <ArrowDownRight size={18}/>,
      title: `${label} ${delta >= 0 ? "improved" : "declined"} by ${Math.abs(delta).toFixed(2)} percentage points.`,
      detail: `${label} moved from ${previous[key].toFixed(2)}% to ${current[key].toFixed(2)}% compared with ${comparisonLabel}.`,
    });
  }
  const belowTarget = overview.bottomLines.filter(line => line.achievement < 100);
  if (belowTarget.length) {
    insights.push({
      tone: "caution",
      icon: <CircleAlert size={18}/>,
      title: `${belowTarget.length} of the bottom-ranked production lines are below target.`,
      detail: `${belowTarget.slice(0, 3).map(line => `${line.name} (${line.achievement.toFixed(1)}%)`).join(", ")} are under 100% achievement for this range.`,
    });
  }
  const leadingCustomer = overview.customers[0];
  if (leadingCustomer && current.productionActual > 0) {
    const contribution = leadingCustomer.productionActual / current.productionActual * 100;
    insights.push({
      tone: "neutral",
      icon: <Users size={18}/>,
      title: `${leadingCustomer.name} contributes ${contribution.toFixed(1)}% of selected production.`,
      detail: `${formatNumber(leadingCustomer.productionActual)} actual units are attributed to this customer in the selected range.`,
    });
  }
  return insights;
}

function currentConfiguration(filters, chartType, dimension, groupBy, aggregation, selectedMetrics) {
  return {
    filters: {
      plantId: filters.plantId,
      startDate: filters.startDate,
      endDate: filters.endDate,
      process: filters.process,
      customerIds: filters.customerIds.map(Number),
      categoryIds: filters.categoryIds.map(Number),
      lineIds: filters.lineIds.map(Number),
      modelIds: filters.modelIds.map(Number),
    },
    chart: { chartType, dimension, groupBy, aggregation, metrics: selectedMetrics },
  };
}

function shortDate(value) {
  const date = new Date(`${value}T00:00:00`);
  return date.toLocaleDateString("en-IN", { day: "numeric", month: "short" });
}

function compactNumber(value) {
  return Number(value || 0).toLocaleString("en-IN", { notation: "compact", maximumFractionDigits: 1 });
}
