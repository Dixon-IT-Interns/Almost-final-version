/* eslint-disable react-hooks/set-state-in-effect */
import { useEffect, useMemo, useRef, useState } from "react";
import { useNavigate, useParams, useSearchParams } from "react-router-dom";
import {
  AlertCircle, ArrowLeft, CheckCircle2, ClipboardList, Loader2,
  Plus, Send, ToggleLeft, ToggleRight, Trash2, Upload, X
} from "lucide-react";
import { api } from "../../api";
import ContributorLayout from "../../components/contributor/ContributorLayout";
import ProcessTracker from "../../components/contributor/ProcessTracker";
import "./DailyReport.css";

const EMPTY_FORM = {
  monthPlan: "", monthActual: "",
  productionPlan: "", productionActual: "",
  uphTarget: "", uphActual: "",
  upphTarget: "", upphInstalled: "", upphActual: "",
  cphTarget: "", cphActual: "",
  fpyTarget: "", fpyActual: "",
  ftyTarget: "", ftyActual: "",
  rtyTarget: "", rtyActual: "",
  osdTarget: "",
  osdReportingDateValue: "", osdReportingDatePercent: null,
  osdMtdValue: "", osdMtdPercent: null,
  otTarget: "",
  plannedOTManhours: null, unplannedOTManhours: null, actualOTManhours: "",
  openWOQty: "", over7DaysWOBalanceQty: "",
  wipLyingGreater3Days: "", wipLyingGreater7Days: "",
  dailyTRCInQty: "", dailyTRCOutQty: "",
  trcOverallFailureInflowPercent: "", trcLyingOver3DaysCr: "",
  issueDescription: "",
};

const GROUPS = [
  {
    title: "Production",
    rows: [
      { label: "Month Plan", fields: [{ key: "monthPlan", label: "Plan" }, { key: "monthActual", label: "Actual" }] },
      { label: "Production", fields: [{ key: "productionPlan", label: "Plan" }, { key: "productionActual", label: "Actual" }] },
      { label: "UPH", fields: [{ key: "uphTarget", label: "Target" }, { key: "uphActual", label: "Actual" }] },
      { label: "UPPH", fields: [{ key: "upphTarget", label: "Target" }, { key: "upphActual", label: "Actual" }] },
      { label: "CPH", fields: [{ key: "cphTarget", label: "Target" }, { key: "cphActual", label: "Actual" }] },
    ],
  },
  {
    title: "Quality",
    rows: [
      { label: "FPY", fields: [{ key: "fpyTarget", label: "Target" }, { key: "fpyActual", label: "Actual" }] },
      { label: "FTY", fields: [{ key: "ftyTarget", label: "Target" }, { key: "ftyActual", label: "Actual" }] },
      { label: "RTY", fields: [{ key: "rtyTarget", label: "Target" }, { key: "rtyActual", label: "Actual" }] },
    ],
  },
  {
    title: "Operations",
    rows: [
      { label: "OS&D (Reporting Date)", fields: [
        { key: "osdReportingDateValue", label: "Value (₹)" },
        { key: "osdReportingDatePercent", label: "%" },
      ] },
      { label: "OS&D MTD", fields: [
        { key: "osdMtdValue", label: "Value (₹)" },
        { key: "osdMtdPercent", label: "%" },
      ] },
      { label: "OT (Hrs)", fields: [
        { key: "plannedOTManhours", label: "Planned" },
        { key: "unplannedOTManhours", label: "Unplanned" },
      ] },
      { label: "Open WO (Qty)", fields: [{ key: "openWOQty", label: "Value" }] },
      { label: ">7 Days WO (Qty)", fields: [{ key: "over7DaysWOBalanceQty", label: "Value" }] },
      { label: "WIP Lying >3 Days (Cr)", fields: [{ key: "wipLyingGreater3Days", label: "Value" }] },
      { label: "WIP Lying >7 Days (Cr)", fields: [{ key: "wipLyingGreater7Days", label: "Value" }] },
    ],
  },
  {
    title: "TRC",
    rows: [
      { label: "Daily TRC In (Qty)", fields: [{ key: "dailyTRCInQty", label: "Value" }] },
      { label: "Daily TRC Out (Qty)", fields: [{ key: "dailyTRCOutQty", label: "Value" }] },
      { label: "TRC Overall Failure Inflow %", fields: [{ key: "trcOverallFailureInflowPercent", label: "%" }] },
      { label: "TRC Lying >3 Days (Cr)", fields: [{ key: "trcLyingOver3DaysCr", label: "Value" }] },
    ],
  },
  { title: "Issue", rows: [{ label: "Issue Description", fields: [{ key: "issueDescription", label: "Description" }] }] },
];

const KPI_PLAN_FIELDS = [
  "productionPlan", "uphTarget", "upphTarget", "cphTarget",
  "fpyTarget", "ftyTarget", "rtyTarget", "osdTarget", "otTarget",
];

const TARGET_FIELDS = new Set([
  "monthPlan", ...KPI_PLAN_FIELDS,
]);
const NON_NUMERIC_MARKERS = new Set(["N/P", "N/A"]);

const PERCENTAGE_FIELDS = new Set([
  "fpyTarget", "fpyActual", "ftyTarget", "ftyActual", "rtyTarget", "rtyActual",
  "osdReportingDatePercent", "osdMtdPercent", "trcOverallFailureInflowPercent",
]);

const INTEGER_FIELDS = new Set([
  "openWOQty", "over7DaysWOBalanceQty", "dailyTRCInQty", "dailyTRCOutQty",
  "wipLyingGreater3Days", "wipLyingGreater7Days",
]);

const NUMERIC_FIELDS = new Set([
  "monthPlan", "monthActual", "productionPlan", "productionActual",
  "uphTarget", "uphActual", "upphTarget", "upphInstalled", "upphActual",
  "cphTarget", "cphActual", "fpyTarget", "fpyActual", "ftyTarget", "ftyActual",
  "rtyTarget", "rtyActual", "osdTarget", "osdReportingDateValue",
  "osdReportingDatePercent", "osdMtdValue", "osdMtdPercent", "otTarget",
  "plannedOTManhours", "unplannedOTManhours", "actualOTManhours",
  "openWOQty", "over7DaysWOBalanceQty", "wipLyingGreater3Days",
  "wipLyingGreater7Days", "dailyTRCInQty", "dailyTRCOutQty",
  "trcOverallFailureInflowPercent", "trcLyingOver3DaysCr",
]);

const ACTUAL_TARGET_FIELDS = {
  monthActual: "monthPlan",
  productionActual: "productionPlan",
  uphActual: "uphTarget",
  upphActual: "upphTarget",
  cphActual: "cphTarget",
  fpyActual: "fpyTarget",
  ftyActual: "ftyTarget",
  rtyActual: "rtyTarget",
  actualOTManhours: "otTarget",
};

function getFieldValidation(form, field) {
  const rawValue = form?.[field];
  if (rawValue === null || rawValue === undefined || String(rawValue).trim() === "")
    return { status: "empty", message: "Not entered" };

  if (field === "issueDescription")
    return { status: "valid", message: "Entered" };

  if (["N/P", "N/A"].includes(String(rawValue).trim().toUpperCase()))
    return { status: "valid", message: String(rawValue).trim().toUpperCase() };

  const value = Number(rawValue);
  if (!Number.isFinite(value))
    return { status: "invalid", message: "Enter a number, N/P or N/A." };
  if (value < 0)
    return { status: "invalid", message: "Value cannot be below 0." };
  if (PERCENTAGE_FIELDS.has(field) && value > 100)
    return { status: "invalid", message: "Percentage must be between 0 and 100." };
  if (INTEGER_FIELDS.has(field) && !Number.isInteger(value))
    return { status: "invalid", message: "Quantity must be a whole number." };

  if (TARGET_FIELDS.has(field))
    return { status: "target", message: "Plan / target" };

  const targetField = ACTUAL_TARGET_FIELDS[field];
  const target = targetField ? Number(form?.[targetField]) : null;
  const targetIsValid = targetField
    && form?.[targetField] !== ""
    && form?.[targetField] !== null
    && form?.[targetField] !== undefined
    && Number.isFinite(target)
    && target >= 0
    && (!PERCENTAGE_FIELDS.has(targetField) || target <= 100);
  if (targetIsValid && value > target)
    return { status: "invalid", message: `Actual is above its target (${target}).` };

  return { status: "valid", message: "Within allowed range" };
}

function clearValidationOverrides(overrides, changedFields) {
  const next = { ...overrides };
  for (const field of changedFields) {
    delete next[field];
    Object.entries(ACTUAL_TARGET_FIELDS)
      .filter(([, targetField]) => targetField === field)
      .forEach(([actualField]) => delete next[actualField]);
  }
  return next;
}

const UPLOAD_TYPES = [
  { value: "ie", label: "IE", group: "Production" },
  { value: "production", label: "Production", group: "Production" },
  { value: "quality", label: "Quality", group: "Quality" },
  { value: "trc", label: "TRC", group: "TRC" },
  { value: "woClosure", label: "WO Closure", group: "Operations" },
  { value: "all", label: "Complete Excel", group: null },
];

const UPLOAD_FIELD_KEYS = {
  ie: ["cphTarget", "cphActual"],
  production: ["monthPlan", "monthActual", "productionPlan", "productionActual", "uphTarget", "uphActual", "upphTarget", "upphInstalled", "upphActual", "cphTarget", "cphActual", "osdReportingDateValue", "osdReportingDatePercent", "osdMtdValue", "osdMtdPercent", "issueDescription"],
  quality: ["fpyTarget", "fpyActual", "ftyTarget", "ftyActual", "rtyTarget", "rtyActual"],
  trc: ["dailyTRCInQty", "dailyTRCOutQty", "trcOverallFailureInflowPercent", "trcLyingOver3DaysCr"],
  woClosure: [],
};

const UPLOAD_LOCK_FIELD_KEYS = {
  ...UPLOAD_FIELD_KEYS,
  woClosure: [
    "openWOQty", "over7DaysWOBalanceQty",
    "wipLyingGreater3Days", "wipLyingGreater7Days",
  ],
};

function uploadTypeInfo(value) {
  return UPLOAD_TYPES.find((item) => item.value === value) || UPLOAD_TYPES[0];
}

function fieldsForUploadType(value) {
  return value === "all" ? null : new Set(UPLOAD_FIELD_KEYS[value] || []);
}

function filterImportedForm(form, value) {
  const allowed = fieldsForUploadType(value);
  const source = { ...(form || {}), ...(form?.nonNumericValues || {}) };
  delete source.presentFields;
  delete source.nonNumericValues;
  return Object.fromEntries(Object.entries(source).filter(([field]) => (
    !allowed || allowed.has(field)
  )));
}

function groupUploadType(title) {
  if (title === "Production") return "production";
  if (title === "Quality") return "quality";
  if (title === "Issue") return "production";
  if (title === "TRC") return "trc";
  if (title === "Operations") return "woClosure";
  return title.toLowerCase();
}

function isFieldLocked(fieldKey, lockedTypes) {
  return lockedTypes.includes("all")
    || Object.entries(UPLOAD_LOCK_FIELD_KEYS).some(([type, fields]) => (
      lockedTypes.includes(type) && fields.includes(fieldKey)
    ));
}

const FATP_IMPORT_PREVIEW_FIELDS = [
  ["Month Plan", "monthPlan"], ["Month Actual", "monthActual"],
  ["Production Plan", "productionPlan"], ["Production Actual", "productionActual"],
  ["UPH Target", "uphTarget"], ["UPH Actual", "uphActual"],
  ["UPPH Installed", "upphInstalled"], ["UPPH Actual", "upphActual"],
  ["OS&D Reporting Date", "osdReportingDateValue"],
  ["OS&D Reporting Date %", "osdReportingDatePercent"],
  ["OS&D MTD", "osdMtdValue"], ["OS&D MTD %", "osdMtdPercent"],
  ["Open WO Qty", "openWOQty"], [">7 Days WO Qty", "over7DaysWOBalanceQty"],
  ["WIP Lying >3 Days", "wipLyingGreater3Days"], ["WIP Lying >7 Days", "wipLyingGreater7Days"],
  ["Issue", "issueDescription"],
];

const IMPORT_PREVIEW_FIELDS = [
  ...FATP_IMPORT_PREVIEW_FIELDS,
  ["UPPH Target", "upphTarget"],
  ["CPH Target", "cphTarget"], ["CPH Actual", "cphActual"],
  ["FPY Target", "fpyTarget"], ["FPY Actual", "fpyActual"],
  ["FTY Target", "ftyTarget"], ["FTY Actual", "ftyActual"],
  ["RTY Target", "rtyTarget"], ["RTY Actual", "rtyActual"],
  ["OS&D Target", "osdTarget"], ["OT Target", "otTarget"],
  ["Planned OT", "plannedOTManhours"], ["Unplanned OT", "unplannedOTManhours"],
  ["Actual OT", "actualOTManhours"], ["Daily TRC In", "dailyTRCInQty"],
  ["Daily TRC Out", "dailyTRCOutQty"], ["TRC Failure Inflow %", "trcOverallFailureInflowPercent"],
  ["TRC Lying >3 Days", "trcLyingOver3DaysCr"],
];

function today() {
  const d = new Date();
  return `${d.getFullYear()}-${String(d.getMonth() + 1).padStart(2, "0")}-${String(d.getDate()).padStart(2, "0")}`;
}

function validReportDate(value) {
  if (!/^\d{4}-\d{2}-\d{2}$/.test(value || "")) return false;
  const [year, month, day] = value.split("-").map(Number);
  const parsed = new Date(year, month - 1, day);
  return parsed.getFullYear() === year
    && parsed.getMonth() === month - 1
    && parsed.getDate() === day;
}

function normalizeCustomerName(value) {
  return String(value || "").replace(/[^a-z0-9]/gi, "").toUpperCase();
}

function isUnspecifiedCustomer(value) {
  const normalized = normalizeCustomerName(value);
  return !normalized || normalized === "NA";
}

function asNumber(value) {
  if (value === "" || value === null || value === undefined) return null;
  const n = Number(value);
  return Number.isFinite(n) ? n : null;
}

function asInteger(value) {
  const n = asNumber(value);
  return n === null ? null : Math.round(n);
}

function prefillPlanTargets(form, plan, editedFields = []) {
  if (!plan) return form;
  const edited = new Set(editedFields);
  const next = { ...form };
  for (const field of KPI_PLAN_FIELDS) {
    if (!edited.has(field) && (next[field] === "" || next[field] === null || next[field] === undefined))
      next[field] = plan[field] ?? "";
  }
  return next;
}

export default function DailyReport({ session, onSignOut }) {
  const navigate = useNavigate();
  const { customerId, categoryId } = useParams();
  const [searchParams, setSearchParams] = useSearchParams();
  const [customer, setCustomer] = useState(null);
  const [category, setCategory] = useState(null);
  const [lines, setLines] = useState([]);
  const [lineData, setLineData] = useState({});
  const [reportDate, setReportDate] = useState(() => (
    validReportDate(searchParams.get("date")) ? searchParams.get("date") : today()
  ));
  const [loading, setLoading] = useState(true);
  const [busy, setBusy] = useState("");
  const [isImporting, setIsImporting] = useState(false);
  const [importPreview, setImportPreview] = useState(null);
  const [importDateConfirmed, setImportDateConfirmed] = useState(false);
  const [pendingProductionImport, setPendingProductionImport] = useState(null);
  const [uploadType, setUploadType] = useState(() => (
    UPLOAD_TYPES.some((item) => item.value === searchParams.get("uploadType"))
      ? searchParams.get("uploadType")
      : "production"
  ));
  const [lockedUploadTypes, setLockedUploadTypes] = useState([]);
  const [error, setError] = useState("");
  const [notice, setNotice] = useState("");
  const excelFileInput = useRef(null);
  const matrixScrollRef = useRef(null);
  const pendingModelEntryId = useRef("");
  const loadSequence = useRef(0);
  const entryIdSequence = useRef(0);
  const submissionBatch = useRef({ key: "", id: "" });
  const [loadedReportKey, setLoadedReportKey] = useState("");
  const currentReportKey = `${customerId}:${categoryId}:${reportDate}`;
  const reportLoaded = loadedReportKey === currentReportKey;
  const isSmtCategory = category?.categoryName?.trim().toUpperCase() === "SMT";
  const isFATPImport = category?.categoryName?.trim().toUpperCase() === "FATP";
  const reportCategories = customer?.categories || [];
  const fatpCategory = reportCategories.find((item) => item.categoryName?.trim().toUpperCase() === "FATP");
  const smtCategory = reportCategories.find((item) => item.categoryName?.trim().toUpperCase() === "SMT");
  const reportGroups = useMemo(() => GROUPS
    .filter((group) => uploadType === "all"
      || (uploadType === "ie" && group.title === "Production")
      || (uploadType === "production" && ["Production", "Operations"].includes(group.title))
      || groupUploadType(group.title) === uploadType)
    .map((group) => {
      if (group.title === "Operations" && uploadType === "production") {
        const operationalOnlyFields = new Set([
          "openWOQty",
          "over7DaysWOBalanceQty",
          "wipLyingGreater3Days",
          "wipLyingGreater7Days",
        ]);
        return {
          ...group,
          rows: group.rows.filter((row) => !row.fields.some(({ key }) => operationalOnlyFields.has(key))),
        };
      }

      if (group.title === "Operations" && uploadType === "woClosure") {
        const nonWoFields = new Set([
          "osdReportingDateValue",
          "osdReportingDatePercent",
          "osdMtdValue",
          "osdMtdPercent",
          "plannedOTManhours",
          "unplannedOTManhours",
        ]);
        return {
          ...group,
          rows: group.rows.filter((row) => !row.fields.some(({ key }) => nonWoFields.has(key))),
        };
      }

      return group.title === "Production"
        ? {
          ...group,
          rows: group.rows
            .map((row) => ({
              ...row,
              fields: row.fields.filter(({ key }) => {
                if (uploadType === "ie")
                  return key === "cphTarget" || key === "cphActual";
                return isSmtCategory
                  ? key !== "upphTarget" && key !== "upphInstalled" && key !== "upphActual"
                  : key !== "cphTarget" && key !== "cphActual";
              }),
            }))
            .filter((row) => row.fields.length > 0),
        }
        : group;
    }), [isSmtCategory, uploadType]);
  const selectedUploadType = uploadTypeInfo(uploadType);
  const selectedSectionLocked = lockedUploadTypes.includes("all")
    || lockedUploadTypes.includes(uploadType)
    || (uploadType === "all" && lockedUploadTypes.length > 0);
  const uploadLocked = selectedSectionLocked
    || (fieldsForUploadType(uploadType) === null
      ? lockedUploadTypes.length > 0
      : lockedUploadTypes.some((type) => (
        (UPLOAD_LOCK_FIELD_KEYS[type] || []).some((field) => (
          (UPLOAD_LOCK_FIELD_KEYS[uploadType] || []).includes(field)
        ))
      )));
  const unlockUploadType = uploadType === "all" || lockedUploadTypes.includes("all")
    ? "all"
    : uploadType;

  const fetchKpiPlan = async (lineId, modelId, date = reportDate) => {
    const query = new URLSearchParams({
      customerId,
      categoryId,
      lineId: String(lineId),
      modelId: String(modelId),
      reportDate: date,
    });
    return api.get(`/daily-reports/kpi-plan?${query.toString()}`);
  };

  const loadReport = async (showLoader = true) => {
    const sequence = ++loadSequence.current;
    if (showLoader) setLoading(true);
    setError("");
    try {
      const customers = await api.get("/catalog/customers");
      const foundCustomer = customers.find((x) => Number(x.customerId) === Number(customerId));
      const foundCategory = foundCustomer?.categories?.find((x) => Number(x.categoryId) === Number(categoryId));
      if (!foundCustomer || !foundCategory) throw new Error("Brand or category configuration was not found.");

      const statusRows = await api.get(`/daily-reports/line-status?customerId=${customerId}&categoryId=${categoryId}&reportDate=${reportDate}`);
      const reviewerAnnotations = await api.get(`/daily-reports/annotations?customerId=${customerId}&categoryId=${categoryId}&reportDate=${reportDate}`);
      const uploadLocks = await api.get(`/daily-reports/upload-locks?customerId=${customerId}&categoryId=${categoryId}&reportDate=${reportDate}`);
      const catalogLines = Array.isArray(foundCategory.productionLines) ? foundCategory.productionLines : [];
      const mergedLines = catalogLines.map((line) => ({
        ...line,
        ...(statusRows.find((x) => Number(x.lineId) === Number(line.lineId)) || {}),
      }));

      const result = await Promise.all(mergedLines.map(async (line) => {
        const [config, entriesResponse] = await Promise.all([
          api.get(`/daily-reports/config?customerId=${customerId}&categoryId=${categoryId}&lineId=${line.lineId}&reportDate=${reportDate}`),
          api.get(`/daily-reports/entries?customerId=${customerId}&categoryId=${categoryId}&lineId=${line.lineId}&reportDate=${reportDate}`),
        ]);
        const saved = Array.isArray(entriesResponse) ? entriesResponse : [];
        const entries = await Promise.all(saved
          .filter((item) => item.modelId !== null && item.modelId !== undefined)
          .map(async (item) => {
            let plan = null;
            let planStatus;
            let planError = "";
            try {
              const response = await fetchKpiPlan(line.lineId, item.modelId);
              plan = response?.plan || null;
              planStatus = plan ? "ready" : "missing";
            } catch (e) {
              planError = e.message || "Unable to load this monthly KPI plan.";
              planStatus = /more than one|ambiguous/i.test(planError) ? "ambiguous" : "error";
            }

            return {
              entryId: `saved-${item.dailyReportId}`,
              dailyReportId: item.dailyReportId,
              modelId: String(item.modelId),
              modelName: item.modelName || "Model",
              status: item.status || "DRAFT",
              isActive: item.isActive !== false,
              rejectionReason: item.rejectionReason || "",
              submissionBatchId: item.submissionBatchId || null,
              annotations: (reviewerAnnotations || []).filter((annotation) => Number(annotation.dailyReportId) === Number(item.dailyReportId)),
              form: { ...EMPTY_FORM, ...item, ...(item.textValues || {}) },
              initialForm: { ...EMPTY_FORM, ...item, ...(item.textValues || {}) },
              isNew: false,
              plan,
              planStatus,
              planError,
              planEditedFields: [],
            };
          }));

        return [line.lineId, {
          isActive: line.isActive !== false,
          status: line.status || "NOT_STARTED",
          models: Array.isArray(config.models) ? config.models : [],
          entries,
        }];
      }));

      if (sequence !== loadSequence.current) return;
      const savedBatchId = result.flatMap(([, data]) => data.entries).find((entry) => entry.submissionBatchId)?.submissionBatchId;
      submissionBatch.current = { key: `${customerId}:${categoryId}:${reportDate}`, id: savedBatchId || "" };
      setCustomer(foundCustomer);
      setCategory(foundCategory);
      setLines(mergedLines);
      setLineData(Object.fromEntries(result));
      setLockedUploadTypes(Array.isArray(uploadLocks) ? uploadLocks : []);
      setLoadedReportKey(`${customerId}:${categoryId}:${reportDate}`);
    } catch (e) {
      if (sequence === loadSequence.current)
        setError(e.message || "Unable to load daily report.");
    } finally {
      if (sequence === loadSequence.current) setLoading(false);
    }
  };

  useEffect(() => {
    loadReport();
    return () => {
      loadSequence.current += 1;
    };
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [customerId, categoryId, reportDate]);

  useEffect(() => {
    if (!pendingModelEntryId.current) return;
    const modelHeader = matrixScrollRef.current?.querySelector(
      `[data-entry-id="${pendingModelEntryId.current}"]`
    );
    if (!modelHeader) return;
    const scrollContainer = matrixScrollRef.current;
    const containerBounds = scrollContainer.getBoundingClientRect();
    const headerBounds = modelHeader.getBoundingClientRect();
    scrollContainer.scrollTo({
      left: scrollContainer.scrollLeft + headerBounds.left - containerBounds.left - 210,
      behavior: "smooth",
    });
    pendingModelEntryId.current = "";
  }, [lineData]);

  const updateEntry = (lineId, entryId, field, value) => {
    setLineData((current) => ({
      ...current,
      [lineId]: {
        ...current[lineId],
        status: "IN_PROGRESS",
        entries: current[lineId].entries.map((entry) => {
          if (entry.entryId !== entryId) return entry;
          const editedFields = new Set(entry.planEditedFields || []);
          const validationOverrides = clearValidationOverrides(
            entry.validationOverrides,
            [field]
          );
          if (KPI_PLAN_FIELDS.includes(field)) {
            if (NON_NUMERIC_MARKERS.has(String(value).trim().toUpperCase()))
              editedFields.delete(field);
            else if (asNumber(value) === asNumber(entry.initialForm?.[field]))
              editedFields.delete(field);
            else if (asNumber(value) !== asNumber(entry.form[field]))
              editedFields.add(field);
          }
          return {
            ...entry,
            status: entry.status === "APPROVED" ? entry.status : "DRAFT",
            form: { ...entry.form, [field]: value },
            planEditedFields: [...editedFields],
            validationOverrides,
          };
        }),
      },
    }));
    setNotice("");
  };

  const changeReportDate = (value) => {
    loadSequence.current += 1;
    setReportDate(value);
    const nextParams = new URLSearchParams(searchParams);
    if (value) nextParams.set("date", value);
    else nextParams.delete("date");
    setSearchParams(nextParams, { replace: true });
  };

  const setValidationOverride = (lineId, entryId, field, accepted) => {
    setLineData((current) => ({
      ...current,
      [lineId]: {
        ...current[lineId],
        entries: current[lineId].entries.map((entry) => {
          if (entry.entryId !== entryId) return entry;
          const validationOverrides = { ...entry.validationOverrides };
          if (accepted) validationOverrides[field] = true;
          else delete validationOverrides[field];
          return { ...entry, validationOverrides };
        }),
      },
    }));
  };

  const setEntryPlan = (lineId, entryId, plan, planStatus, planError = "") => {
    setLineData((current) => ({
      ...current,
      [lineId]: {
        ...current[lineId],
        entries: current[lineId].entries.map((entry) => entry.entryId !== entryId
          ? entry
          : {
              ...entry,
              plan,
              planStatus,
              planError,
              form: entry.isNew ? prefillPlanTargets(entry.form, plan, entry.planEditedFields) : entry.form,
              initialForm: entry.isNew
                ? prefillPlanTargets(entry.initialForm || { ...EMPTY_FORM }, plan, entry.planEditedFields)
                : entry.initialForm,
            }),
      },
    }));
  };

  const addModel = (lineId, modelId) => {
    if (!modelId) return;
    const line = lineData[lineId];
    if (!line) {
      setError("Unable to find the selected production line. Please refresh the report and try again.");
      return;
    }
    if (line?.entries.some((entry) => String(entry.modelId) === String(modelId))) {
      setError(`${line.lineName}: this model is already on the line.`);
      return;
    }
    const model = line?.models.find((item) => String(item.modelId) === String(modelId));
    if (!model) {
      setError("The selected model is not available for this production line. Please refresh the report and try again.");
      return;
    }
    const entryId = `new-${lineId}-${modelId}-${++entryIdSequence.current}`;
    pendingModelEntryId.current = entryId;
    const sequence = loadSequence.current;
    setLineData((current) => {
      const currentLine = current[lineId];
      if (!currentLine || currentLine.entries.some((entry) => String(entry.modelId) === String(modelId))) return current;
      return {
        ...current,
        [lineId]: {
          ...currentLine,
          status: "IN_PROGRESS",
          entries: [...currentLine.entries, {
            entryId,
            dailyReportId: null,
            modelId: String(modelId),
            modelName: model.modelName,
            status: "DRAFT",
            isActive: true,
            rejectionReason: "",
            form: { ...EMPTY_FORM },
            initialForm: { ...EMPTY_FORM },
            isNew: true,
            plan: null,
            planStatus: "loading",
            planError: "",
            planEditedFields: [],
          }],
        },
      };
    });

    fetchKpiPlan(lineId, modelId).then(({ plan }) => {
      if (sequence === loadSequence.current)
        setEntryPlan(lineId, entryId, plan || null, plan ? "ready" : "missing");
    }).catch((e) => {
      if (sequence === loadSequence.current)
        setEntryPlan(
          lineId,
          entryId,
          null,
          /more than one|ambiguous/i.test(e.message || "") ? "ambiguous" : "error",
          e.message || "Unable to load this monthly KPI plan."
        );
    });
  };

  const removeModel = (lineId, entryId) => {
    setLineData((current) => ({
      ...current,
      [lineId]: {
        ...current[lineId],
        entries: current[lineId].entries.filter((entry) => entry.entryId !== entryId),
      },
    }));
  };

  const deleteDraft = async (lineId, entry) => {
    if (!entry?.dailyReportId || !["DRAFT", "REJECTED"].includes(entry.status)) return;
    if (!window.confirm(`Delete the saved draft for ${entry.modelName}? This cannot be undone from the report screen.`)) return;
    setBusy(`delete-${entry.entryId}`);
    setError("");
    setNotice("");
    try {
      await api.delete(`/daily-reports/draft/${entry.dailyReportId}`);
      setLineData((current) => ({
        ...current,
        [lineId]: {
          ...current[lineId],
          entries: current[lineId].entries.filter((item) => item.entryId !== entry.entryId),
          status: "IN_PROGRESS",
        },
      }));
      setNotice(`${entry.modelName} draft deleted.`);
    } catch (e) {
      setError(e.message || "Unable to delete the draft.");
    } finally {
      setBusy("");
    }
  };

  const getLineName = (lineId) => lines.find((x) => Number(x.lineId) === Number(lineId))?.lineName || "Selected line";

  const toggleLineStatus = async (line) => {
    const lineId = Number(line.lineId);
    const data = lineData[lineId];
    if (!data) return;

    const isActive = data.isActive !== false;
    const nextActive = !isActive;
    const entries = data.entries || [];
    if (nextActive === false) {
      if (entries.some((entry) => ["SUBMITTED", "APPROVED"].includes(entry.status))) {
        setError("A line with submitted or approved reports cannot be deactivated.");
        return;
      }
      const hasUnsavedChanges = entries.some((entry) => (
        entry.isNew || Object.keys(entry.form || {}).some((field) => (
          String(entry.form[field] ?? "") !== String(entry.initialForm?.[field] ?? "")
        ))
      ));
      if (hasUnsavedChanges) {
        setError("Save or remove unsaved line entries before deactivating this line.");
        return;
      }
    }

    const action = nextActive ? "activate" : "deactivate";
    const message = nextActive
      ? `Activate ${line.lineName} for ${reportDate}?`
      : `Deactivate ${line.lineName} for ${reportDate}? Saved draft data will be retained and restored when activated again.`;
    if (!window.confirm(message)) return;

    setBusy(`line-status-${lineId}`);
    setError("");
    setNotice("");
    try {
      await api.post("/daily-reports/line-status", {
        customerId: Number(customerId),
        categoryId: Number(categoryId),
        lineId,
        reportDate,
        isActive: nextActive,
      });
      await loadReport(false);
      setNotice(`${line.lineName} ${action}d for ${reportDate}.`);
    } catch (e) {
      setError(e.message || `Unable to ${action} this line.`);
    } finally {
      setBusy("");
    }
  };

  const switchReportCategory = (nextCategory) => {
    if (!nextCategory || Number(nextCategory.categoryId) === Number(categoryId)) return;
    if (importPreview) {
      setError("Apply or cancel the current Excel preview before switching report categories.");
      return;
    }
    const hasUnsavedChanges = Object.values(lineData).some((line) => (
      (line.entries || []).some((entry) => (
        entry.isNew || JSON.stringify(entry.form) !== JSON.stringify(entry.initialForm)
      ))
    ));
    if (hasUnsavedChanges && !window.confirm("You have unsaved changes. Switch report and discard these changes?")) return;
    navigate(`/contributor/brands/${customerId}/categories/${nextCategory.categoryId}/report`);
  };

  const openPendingProductionImport = () => {
    const hasUnsavedChanges = Object.values(lineData).some((line) => (
      (line.entries || []).some((entry) => (
        entry.isNew || JSON.stringify(entry.form) !== JSON.stringify(entry.initialForm)
      ))
    ));
    if (hasUnsavedChanges) {
      setError("Save this category's draft before opening the other category's Production import.");
      return;
    }

    const nextCategory = reportCategories.find(
      (item) => Number(item.categoryId) === Number(pendingProductionImport?.categoryId)
    );
    if (nextCategory)
      navigate(`/contributor/brands/${customerId}/categories/${nextCategory.categoryId}/report`);
  };

  const lockSelectedUploadType = async () => {
    if (selectedSectionLocked) return;
    if (!window.confirm(`Lock ${selectedUploadType.label} for this report date? You will not be able to upload or edit this section afterwards.`)) return;
    setBusy(`lock-${uploadType}`);
    setError("");
    setNotice("");
    try {
      await api.post("/daily-reports/upload-lock", {
        customerId: Number(customerId),
        categoryId: Number(categoryId),
        reportDate,
        uploadType,
      });
      setLockedUploadTypes((current) => current.includes(uploadType) ? current : [...current, uploadType]);
      setNotice(`${selectedUploadType.label} is locked. Select another upload type to continue.`);
    } catch (e) {
      setError(e.message || `Unable to lock ${selectedUploadType.label}.`);
    } finally {
      setBusy("");
    }
  };

  const unlockSelectedUploadType = async () => {
    if (!selectedSectionLocked) return;
    const unlockAll = unlockUploadType === "all";
    const label = unlockAll ? "all locked sections" : selectedUploadType.label;
    if (!window.confirm(`Unlock ${label} for this report date? Its fields will become editable again.`)) return;
    setBusy(`unlock-${unlockUploadType}`);
    setError("");
    setNotice("");
    try {
      const query = new URLSearchParams({
        customerId: String(customerId),
        categoryId: String(categoryId),
        reportDate,
        uploadType: unlockUploadType,
      });
      await api.delete(`/daily-reports/upload-lock?${query.toString()}`);
      setLockedUploadTypes((current) => unlockAll
        ? []
        : current.filter((type) => type !== unlockUploadType));
      setNotice(`${unlockAll ? "All sections" : selectedUploadType.label} unlocked. Its fields are editable again.`);
    } catch (e) {
      setError(e.message || `Unable to unlock ${label}.`);
    } finally {
      setBusy("");
    }
  };

  const buildPayload = (lineId, entry, submissionBatchId = null) => ({
    reportDate,
    submissionBatchId,
    nonNumericValues: Object.fromEntries([...NUMERIC_FIELDS]
      .filter((field) => NON_NUMERIC_MARKERS.has(String(entry.form[field] || "").trim().toUpperCase()))
      .map((field) => [field, String(entry.form[field]).trim().toUpperCase()])),
    customerId: Number(customerId), categoryId: Number(categoryId), lineId: Number(lineId), modelId: Number(entry.modelId),
    kpiPlanId: entry.plan?.planId ?? null,
    kpiPlanUpdatedAt: entry.plan?.updatedAt ?? null,
    planEdits: entry.plan
      ? (entry.planEditedFields || [])
          .filter((field) => KPI_PLAN_FIELDS.includes(field)
            && !NON_NUMERIC_MARKERS.has(String(entry.form[field] || "").trim().toUpperCase())
            && asNumber(entry.form[field]) !== asNumber(entry.plan[field]))
          .map((field) => ({ field, value: asNumber(entry.form[field]) }))
      : [],
    monthPlan: asNumber(entry.form.monthPlan), monthActual: asNumber(entry.form.monthActual),
    productionPlan: asNumber(entry.form.productionPlan), productionActual: asNumber(entry.form.productionActual),
    uphTarget: asNumber(entry.form.uphTarget), uphActual: asNumber(entry.form.uphActual),
    upphTarget: isSmtCategory ? null : asNumber(entry.form.upphTarget),
    upphInstalled: isSmtCategory ? null : asNumber(entry.form.upphInstalled),
    upphActual: isSmtCategory ? null : asNumber(entry.form.upphActual),
    cphTarget: isSmtCategory ? asNumber(entry.form.cphTarget) : null,
    cphActual: isSmtCategory ? asNumber(entry.form.cphActual) : null,
    fpyTarget: asNumber(entry.form.fpyTarget), fpyActual: asNumber(entry.form.fpyActual),
    ftyTarget: asNumber(entry.form.ftyTarget), ftyActual: asNumber(entry.form.ftyActual),
    rtyTarget: asNumber(entry.form.rtyTarget), rtyActual: asNumber(entry.form.rtyActual),
    osdTarget: asNumber(entry.form.osdTarget),
    osdReportingDateValue: asNumber(entry.form.osdReportingDateValue), osdReportingDatePercent: asNumber(entry.form.osdReportingDatePercent),
    osdMtdValue: asNumber(entry.form.osdMtdValue), osdMtdPercent: asNumber(entry.form.osdMtdPercent),
    otTarget: asNumber(entry.form.otTarget),
    plannedOTManhours: asNumber(entry.form.plannedOTManhours), unplannedOTManhours: asNumber(entry.form.unplannedOTManhours),
    actualOTManhours: asNumber(entry.form.actualOTManhours), openWOQty: asInteger(entry.form.openWOQty),
    over7DaysWOBalanceQty: asInteger(entry.form.over7DaysWOBalanceQty), dailyTRCInQty: asInteger(entry.form.dailyTRCInQty),
    wipLyingGreater3Days: asInteger(entry.form.wipLyingGreater3Days), wipLyingGreater7Days: asInteger(entry.form.wipLyingGreater7Days),
    dailyTRCOutQty: asInteger(entry.form.dailyTRCOutQty), trcOverallFailureInflowPercent: asNumber(entry.form.trcOverallFailureInflowPercent),
    trcLyingOver3DaysCr: asNumber(entry.form.trcLyingOver3DaysCr), issueDescription: entry.form.issueDescription?.trim() || null,
  });

  const saveLine = async (lineId, submit) => {
    const line = lineData[lineId];
    if (!line?.isActive) return setError("Activate the line before saving or submitting it.");
    if (!line.entries.length) return setError(`Add at least one model to ${getLineName(lineId)}.`);
    if (new Set(line.entries.map((entry) => String(entry.modelId))).size !== line.entries.length) {
      return setError(`${getLineName(lineId)} contains the same model more than once.`);
    }
    const invalidText = line.entries.flatMap((entry) => [...NUMERIC_FIELDS]
      .filter((field) => {
        const value = entry.form?.[field];
        if (value === "" || value === null || value === undefined) return false;
        const text = String(value).trim().toUpperCase();
        return text !== "N/P" && text !== "N/A" && !Number.isFinite(Number(text));
      })
      .map((field) => `${entry.modelName}: ${field}`));
    if (invalidText.length)
      return setError(`Use a number, N/P or N/A in numeric report fields. Check ${invalidText.slice(0, 3).join(", ")}.`);
    setBusy(`${submit ? "submit" : "save"}-${lineId}`); setError(""); setNotice("");
    try {
      const reportKey = `${customerId}:${categoryId}:${reportDate}`;
      if (submissionBatch.current.key !== reportKey) submissionBatch.current = { key: reportKey, id: "" };
      if (submit && !submissionBatch.current.id) {
        submissionBatch.current.id = globalThis.crypto?.randomUUID?.() || "";
        if (!submissionBatch.current.id) throw new Error("Secure report submission IDs require a modern browser or HTTPS.");
      }
      const batchId = submit ? submissionBatch.current.id : null;
      for (const entry of line.entries) {
        if (!entry.modelId) throw new Error(`Select a model for every column in ${getLineName(lineId)}.`);
        const saved = await api.post(`/daily-reports/${submit ? "submit" : "draft"}`, buildPayload(lineId, entry, batchId));
        const savedStatus = saved?.status || (submit ? "SUBMITTED" : "DRAFT");
        setLineData((current) => {
          const currentLine = current[lineId];
          if (!currentLine) return current;
          const entries = currentLine.entries.map((currentEntry) => currentEntry.entryId !== entry.entryId
            ? currentEntry
            : {
                ...currentEntry,
                dailyReportId: saved?.dailyReportId ?? currentEntry.dailyReportId,
                submissionBatchId: saved?.submissionBatchId ?? batchId ?? currentEntry.submissionBatchId,
                status: savedStatus,
                isNew: false,
                initialForm: { ...entry.form },
                planEditedFields: KPI_PLAN_FIELDS.filter((field) => (
                  asNumber(currentEntry.form[field]) !== asNumber(entry.form[field])
                )),
              });
          const statuses = entries.map((item) => item.status);
          const lineStatus = statuses.every((status) => status === "APPROVED")
            ? "APPROVED"
            : statuses.every((status) => status === "SUBMITTED" || status === "APPROVED")
              ? "SUBMITTED"
              : "IN_PROGRESS";
          return { ...current, [lineId]: { ...currentLine, status: lineStatus, entries } };
        });

      }
      setNotice(`${getLineName(lineId)} ${submit ? "submitted for approval" : "draft saved"}.`);
    } catch (e) {
      setError(e.message || "Unable to save the report.");
    } finally { setBusy(""); }
  };

  function stageSmtPreview(preview) {
    const pageLines = new Map(lines.map((line) => [Number(line.lineId), line]));
    const validationErrors = [];

    for (const imported of preview.data || []) {
      const line = pageLines.get(Number(imported.lineId));
      const lineState = lineData[imported.lineId];
      if (!line || !lineState) {
        validationErrors.push(`Line ${imported.lineNo} is not available on this report page.`);
        continue;
      }

      const modelAvailable = lineState.models.some(
        (model) => Number(model.modelId) === Number(imported.modelId)
      );
      if (!modelAvailable) {
        validationErrors.push(
          `Model ${imported.modelName} is not available for line ${imported.lineNo} on this page.`
        );
      }

      const existingEntry = lineState.entries.find(
        (entry) => Number(entry.modelId) === Number(imported.modelId)
      );
      if (existingEntry?.status === "APPROVED" || existingEntry?.status === "SUBMITTED") {
        validationErrors.push(
          `Model ${imported.modelName} on line ${imported.lineNo} has already been submitted and cannot be replaced.`
        );
      }
    }

    if (validationErrors.length) throw new Error(validationErrors.join(" "));

    const importedNewModels = (preview.data || []).filter((imported) => (
      !lineData[imported.lineId]?.entries.some(
        (entry) => Number(entry.modelId) === Number(imported.modelId)
      )
    ));
    const importedEntryIds = new Map(importedNewModels.map((imported) => [
      `${imported.lineId}:${imported.modelId}`,
      `import-${imported.lineId}-${imported.modelId}-${++entryIdSequence.current}`,
    ]));

    setLineData((current) => {
      const next = { ...current };
      const grouped = new Map();

      for (const imported of preview.data || []) {
        const lineId = Number(imported.lineId);
        const entries = grouped.get(lineId) || [];
        entries.push(imported);
        grouped.set(lineId, entries);
      }

      for (const [lineId, importedEntries] of grouped) {
        const currentLine = next[lineId];
        const entries = [...currentLine.entries];

        for (const imported of importedEntries) {
          const existingIndex = entries.findIndex(
            (entry) => Number(entry.modelId) === Number(imported.modelId)
          );
          if (existingIndex >= 0) {
            const existing = entries[existingIndex];
            const presentFields = new Set(imported.form?.presentFields || Object.keys(imported.form || {}));
            const formValues = filterImportedForm(
              Object.fromEntries(Object.entries(imported.form || {}).filter(([field]) => presentFields.has(field) || field === "nonNumericValues")),
              uploadType
            );
            const editedFields = new Set(existing.planEditedFields || []);
            for (const field of KPI_PLAN_FIELDS) {
              if (formValues[field] === null || formValues[field] === undefined || formValues[field] === "")
                delete formValues[field];
              else if (NON_NUMERIC_MARKERS.has(String(formValues[field]).trim().toUpperCase()))
                editedFields.delete(field);
              else if (asNumber(formValues[field]) === asNumber(existing.initialForm?.[field]))
                editedFields.delete(field);
              else
                editedFields.add(field);
            }
            entries[existingIndex] = {
              ...existing,
              form: { ...existing.form, ...formValues },
              status: "DRAFT",
              planEditedFields: [...editedFields],
              validationOverrides: clearValidationOverrides(
                existing.validationOverrides,
                Object.keys(formValues)
              ),
            }
          } else {
            const presentFields = new Set(imported.form?.presentFields || Object.keys(imported.form || {}));
            const importedForm = filterImportedForm(
              Object.fromEntries(Object.entries(imported.form || {}).filter(([field]) => presentFields.has(field) || field === "nonNumericValues")),
              uploadType
            );
            const form = { ...EMPTY_FORM, ...importedForm };
            entries.push({
              entryId: importedEntryIds.get(`${lineId}:${imported.modelId}`),
              dailyReportId: null,
              modelId: String(imported.modelId),
              modelName: imported.modelName,
              status: "DRAFT",
              isActive: true,
              rejectionReason: "",
              form,
              initialForm: { ...EMPTY_FORM },
              isNew: true,
              plan: null,
              planStatus: "not-applicable",
              planError: "",
              planEditedFields: [],
            });
          }
        }

        next[lineId] = { ...currentLine, status: "IN_PROGRESS", entries };
      }

      return next;
    });

    const importWarnings = (preview.errors || []).slice(0, 3);
    const remainingWarnings = Math.max(0, (preview.errors || []).length - importWarnings.length);
    setNotice(
      `Excel imported successfully — ${preview.lines} lines, ${preview.models} models. Review the staged values, then save each line as a draft.`
      + (importWarnings.length
        ? ` Import notes: ${importWarnings.join(" ")}${remainingWarnings ? ` And ${remainingWarnings} more row/field note(s).` : ""}`
        : "")
    );
  };

  useEffect(() => {
    if (!reportLoaded || importPreview) return;
    const queueKey = `dcc-production-import:${customerId}:${categoryId}`;
    const stored = sessionStorage.getItem(queueKey);
    if (!stored) return;

    try {
      const queued = JSON.parse(stored);
      if (Number(queued.customerId) !== Number(customerId)
          || Number(queued.categoryId) !== Number(categoryId)
          || !queued.preview?.reportDate) {
        sessionStorage.removeItem(queueKey);
        return;
      }

      if (queued.preview.reportDate !== reportDate) {
        loadSequence.current += 1;
        setReportDate(queued.preview.reportDate);
        return;
      }

      if (isFATPImport) {
        if (!Array.isArray(queued.preview.rows) || queued.preview.rows.length === 0)
          throw new Error("The queued FATP production preview contains no rows.");
        setImportPreview({
          ...queued.preview,
          queuedProduction: queued.queuedProduction === true,
          rows: queued.preview.rows.map((row) => ({
            ...row,
            include: row.status === "matched" && !lineData[row.lineId]?.entries?.some(
              (entry) => Number(entry.modelId) === Number(row.modelId)
                && ["SUBMITTED", "APPROVED"].includes(entry.status)
            ),
            selectedLineId: row.lineId ? String(row.lineId) : "",
            selectedModelId: row.modelId ? String(row.modelId) : "",
          })),
        });
        setImportDateConfirmed(true);
        if (queued.queuedProduction === true)
          setPendingProductionImport(null);
        setNotice(`FATP rows from the Production workbook are ready for ${queued.preview.reportDate}. Review and apply them; nothing has been saved.`);
      } else {
        try {
          stageSmtPreview(queued.preview);
          sessionStorage.removeItem(queueKey);
          if (queued.queuedProduction === true)
            setPendingProductionImport(null);
        } catch (e) {
          sessionStorage.removeItem(queueKey);
          if (queued.queuedProduction === true)
            setPendingProductionImport(null);
          throw e;
        }
      }
    } catch (e) {
      setError(e.message || "Unable to load the queued production import.");
    }
    // The helper is local and recreated per render; the other dependencies keep its captured report data current.
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [reportLoaded, customerId, categoryId, reportDate, isFATPImport, lineData, importPreview]);

  const importExcel = async (event) => {
    const file = event.target.files?.[0];
    if (!file) return;

    const sequence = loadSequence.current;
    setIsImporting(true);
    setError("");
    setNotice("");

    try {
      const importCategories = fatpCategory && smtCategory
        ? [fatpCategory, smtCategory]
        : [category];
      const previews = await Promise.all(importCategories.map(async (targetCategory) => {
        const formData = new FormData();
        formData.append("file", file);
        formData.append("customerId", String(customerId));
        formData.append("categoryId", String(targetCategory.categoryId));
        formData.append("reportDate", reportDate);
        formData.append("uploadType", uploadType);
        const result = await api.postFormData("/daily-reports/import/preview", formData);
        return [Number(targetCategory.categoryId), result];
      }));
      if (sequence !== loadSequence.current)
        throw new Error("The report context changed while Excel was being read. Please upload the file again.");

      const previewByCategory = new Map(previews);
      const preview = previewByCategory.get(Number(categoryId));
      if (!preview) throw new Error("The workbook preview for the selected report category is missing.");

      for (const [targetCategoryId, result] of previews) {
        if (targetCategoryId === Number(categoryId)) continue;
        const targetCategory = reportCategories.find(
          (item) => Number(item.categoryId) === targetCategoryId
        );
        const queueKey = `dcc-production-import:${customerId}:${targetCategoryId}`;
        sessionStorage.setItem(queueKey, JSON.stringify({
          customerId: Number(customerId),
          categoryId: targetCategoryId,
          categoryName: targetCategory?.categoryName || "Production",
          queuedProduction: true,
          preview: result,
        }));
        setPendingProductionImport({
          categoryId: targetCategoryId,
          categoryName: targetCategory?.categoryName || "other category",
          reportDate: result.reportDate,
        });
      }

      if (isFATPImport) {
        if (!Array.isArray(preview.rows) || preview.rows.length === 0)
          throw new Error("The FATP production table did not contain any rows to review.");
        setImportPreview({
          ...preview,
          rows: preview.rows.map((row) => ({
            ...row,
            include: row.status === "matched" && !lineData[row.lineId]?.entries?.some(
              (entry) => Number(entry.modelId) === Number(row.modelId)
                && ["SUBMITTED", "APPROVED"].includes(entry.status)
            ),
            selectedLineId: row.lineId ? String(row.lineId) : "",
            selectedModelId: row.modelId ? String(row.modelId) : "",
          })),
        });
        setImportDateConfirmed(preview.reportDate === reportDate);
        setNotice("Production workbook loaded. Review FATP rows here; SMT rows are ready in the other category. Nothing has been saved.");
        return;
      }

      if (preview.reportDate !== reportDate) {
        const queueKey = `dcc-production-import:${customerId}:${categoryId}`;
        sessionStorage.setItem(queueKey, JSON.stringify({
          customerId: Number(customerId),
          categoryId: Number(categoryId),
          categoryName: category?.categoryName || "SMT",
          queuedProduction: false,
          preview,
        }));
        loadSequence.current += 1;
        setReportDate(preview.reportDate);
        setNotice(`SMT rows were read for ${preview.reportDate}. Loading that report date before staging them.`);
        return;
      }

      stageSmtPreview(preview);
    } catch (e) {
      const otherCategory = reportCategories.find(
        (item) => Number(item.categoryId) !== Number(categoryId)
          && ["FATP", "SMT"].includes(item.categoryName?.trim().toUpperCase())
      );
      if (otherCategory) {
        sessionStorage.removeItem(`dcc-production-import:${customerId}:${otherCategory.categoryId}`);
        setPendingProductionImport(null);
      }
      setError(
        e.message?.startsWith("Excel import failed")
          ? e.message
          : `Excel import failed: ${e.message || "Unable to read the workbook."}`
      );
    } finally {
      setIsImporting(false);
      event.target.value = "";
    }
  };

  const updateImportRow = (rowIndex, changes) => {
    setImportPreview((current) => current
      ? {
          ...current,
          rows: current.rows.map((row, index) => index === rowIndex ? { ...row, ...changes } : row),
        }
      : current);
  };

  const applyReviewedImport = () => {
    if (!importPreview) return;
    const needsDateConfirmation = importPreview.reportDate !== reportDate;
    if (needsDateConfirmation && !importDateConfirmed) {
      setError("Confirm the workbook report date before applying the FATP rows.");
      return;
    }
    if (importPreview.reportDate !== reportDate || loading || !reportLoaded) {
      setError("The report is still loading for the workbook date. Wait for loading to finish, then apply.");
      return;
    }

    const selected = importPreview.rows.filter((row) => row.include);
    if (!selected.length) {
      setError("Select at least one matched or manually mapped FATP row to apply.");
      return;
    }

    const normalizedCustomer = (value) => String(value || "").replace(/[^a-z0-9]/gi, "").toUpperCase();
    const errors = [];
    const mapped = selected.map((row) => {
      const lineId = Number(row.selectedLineId);
      const modelId = Number(row.selectedModelId);
      const line = lineData[lineId];
      const model = line?.models?.find((item) => Number(item.modelId) === modelId);
      if (!isUnspecifiedCustomer(row.customer)
        && normalizedCustomer(row.customer) !== normalizedCustomer(customer?.customerName)) {
        errors.push(`Excel row ${row.rowNumber} does not identify the selected brand.`);
      }
      if (!line || line.isActive === false || !model) {
        errors.push(`Excel row ${row.rowNumber} must be mapped to an active line and model for this report.`);
      }
      const existing = line?.entries?.find((entry) => Number(entry.modelId) === modelId);
      if (existing && ["SUBMITTED", "APPROVED"].includes(existing.status)) {
        errors.push(`Excel row ${row.rowNumber} targets ${line.lineName} / ${model?.modelName}, which is locked.`);
      }
      return { row, lineId, modelId, line, model, existing };
    });

    const duplicateKeys = new Set();
    const seenMappings = new Set();
    for (const item of mapped) {
      const key = `${item.lineId}:${item.modelId}`;
      if (seenMappings.has(key)) duplicateKeys.add(key);
      seenMappings.add(key);
    }
    if (duplicateKeys.size) {
      errors.push("The reviewed rows contain duplicate line/model mappings. Skip duplicate rows before applying.");
    }
    if (errors.length) {
      setError(errors.join(" "));
      return;
    }

    const importedRows = mapped.map(({ row, lineId, modelId, model }) => ({
      lineId,
      modelId,
      modelName: model.modelName,
      form: filterImportedForm(
        Object.fromEntries(Object.entries(row.form || {}).filter(([, value]) => value !== null && value !== undefined && value !== "")),
        uploadType
      ),
    }));
    const newModels = importedRows.filter((imported) => (
      !lineData[imported.lineId]?.entries.some((entry) => Number(entry.modelId) === imported.modelId)
    ));
    const newEntryIds = new Map(newModels.map((imported) => [
      `${imported.lineId}:${imported.modelId}`,
      `reviewed-import-${imported.lineId}-${imported.modelId}-${++entryIdSequence.current}`,
    ]));

    setLineData((current) => {
      const next = { ...current };
      const grouped = new Map();
      for (const imported of importedRows) {
        const entries = grouped.get(imported.lineId) || [];
        entries.push(imported);
        grouped.set(imported.lineId, entries);
      }

      for (const [lineId, imports] of grouped) {
        const currentLine = next[lineId];
        if (!currentLine) continue;
        const entries = [...currentLine.entries];
        for (const imported of imports) {
          const index = entries.findIndex((entry) => Number(entry.modelId) === imported.modelId);
          if (index >= 0) {
            const existing = entries[index];
            const form = { ...imported.form };
            const editedFields = new Set(existing.planEditedFields || []);
            for (const field of KPI_PLAN_FIELDS) {
              if (!(field in form)) continue;
              if (NON_NUMERIC_MARKERS.has(String(form[field]).trim().toUpperCase()))
                editedFields.delete(field);
              else if (asNumber(form[field]) === asNumber(existing.initialForm?.[field]))
                editedFields.delete(field);
              else
                editedFields.add(field);
            }
            entries[index] = {
              ...existing,
              form: { ...existing.form, ...form },
              planEditedFields: [...editedFields],
            };
          } else {
            const form = { ...EMPTY_FORM, ...imported.form };
            entries.push({
              entryId: newEntryIds.get(`${lineId}:${imported.modelId}`),
              dailyReportId: null,
              modelId: String(imported.modelId),
              modelName: imported.modelName,
              status: "DRAFT",
              isActive: true,
              rejectionReason: "",
              form,
              initialForm: { ...EMPTY_FORM },
              isNew: true,
              plan: null,
              planStatus: "not-applicable",
              planError: "",
              planEditedFields: [],
            });
          }
        }
        next[lineId] = { ...currentLine, status: "IN_PROGRESS", entries };
      }
      return next;
    });

    setImportPreview(null);
    setImportDateConfirmed(false);
    if (importPreview.queuedProduction) {
      sessionStorage.removeItem(`dcc-production-import:${customerId}:${categoryId}`);
      setPendingProductionImport(null);
    }
    setError("");
    setNotice(`Applied ${importedRows.length} reviewed FATP row(s) to the form. Review and save or submit manually.`);
  };

  const cancelImportPreview = () => {
    if (importPreview?.queuedProduction) {
      sessionStorage.removeItem(`dcc-production-import:${customerId}:${categoryId}`);
      setPendingProductionImport(null);
    }
    setImportPreview(null);
    setImportDateConfirmed(false);
  };

  const completed = lines.filter((line) => lineData[line.lineId]?.isActive === false
    || ["SUBMITTED", "APPROVED"].includes(lineData[line.lineId]?.status)).length;

  return (
    <ContributorLayout user={session?.user} onSignOut={onSignOut}>
      <main className="daily-report-page">
        <div className="daily-report-content">
          <ProcessTracker activeStep="report" customerId={customerId} categoryId={categoryId} />

          <button className="report-back" onClick={() => navigate("/contributor/brands")}>
            <ArrowLeft size={16} /> Back to customers
          </button>

          <div className="report-breadcrumb">{customer?.customerName || "Brand"}<span>›</span>{category?.categoryName || "FATP"}<span>›</span><strong>Daily Report</strong></div>

          <header className="report-title-row">
            <div className="report-title-icon"><ClipboardList size={25} /></div>
            <div><h1>{category?.categoryName || "Daily"} Daily Report</h1></div>
            {fatpCategory && smtCategory && (
              <label className="report-category-switch">
                <span>REPORT TYPE</span>
                <select
                  aria-label="Select FATP or SMT report"
                  value={String(categoryId)}
                  onChange={(event) => {
                    const nextCategory = reportCategories.find(
                      (item) => Number(item.categoryId) === Number(event.target.value)
                    );
                    switchReportCategory(nextCategory);
                  }}
                >
                  <option value={fatpCategory.categoryId}>FATP</option>
                  <option value={smtCategory.categoryId}>SMT</option>
                </select>
              </label>
            )}
            <label className="report-upload-type">
              <span>UPLOAD TYPE</span>
              <select aria-label="Select Excel upload type" value={uploadType} onChange={(event) => setUploadType(event.target.value)} disabled={isImporting || Boolean(busy)}>
                <option value="production">Production · SMT + FATP</option>
                {UPLOAD_TYPES.filter((item) => item.value !== "production").map((item) => <option key={item.value} value={item.value}>{item.label}</option>)}
              </select>
            </label>
            <button
              className="secondary-action report-excel-upload"
              type="button"
              style={{ marginLeft: "auto", flexShrink: 0 }}
              disabled={loading || !reportLoaded || isImporting || Boolean(busy) || uploadLocked || Boolean(pendingProductionImport) || Boolean(importPreview)}
              onClick={() => excelFileInput.current?.click()}
            >
              {isImporting ? <Loader2 size={14} className="spin" /> : <Upload size={14} />}
              {isImporting ? "Reading Excel..." : "Upload Excel"}
            </button>
            <button
              className="secondary-action report-lock-upload"
              type="button"
              disabled={loading || !reportLoaded || isImporting || Boolean(busy)}
              onClick={selectedSectionLocked ? unlockSelectedUploadType : lockSelectedUploadType}
            >
              {selectedSectionLocked
                ? `Unlock ${unlockUploadType === "all" ? "All Sections" : selectedUploadType.label}`
                : `Lock ${selectedUploadType.label}`}
            </button>
            <input
              ref={excelFileInput}
              type="file"
              accept=".xlsx,application/vnd.openxmlformats-officedocument.spreadsheetml.sheet"
              hidden
              onChange={importExcel}
            />
          </header>

          <div className="report-context-bar">
            <div><span>BRAND</span><strong>{customer?.customerName || "—"}</strong></div>
            <div><span>CATEGORY</span><strong>{category?.categoryName || "—"}</strong></div>
            <label><span>REPORT DATE</span><input type="date" max={today()} value={reportDate} onChange={(e) => changeReportDate(e.target.value)} /></label>
            <div><span>PROGRESS</span><strong>{completed}/{lines.length} lines completed</strong></div>
          </div>

          <div className="upload-section-status" aria-label="Upload section status">
            {UPLOAD_TYPES.filter((item) => item.value !== "all").map((item) => (
              <span key={item.value} className={lockedUploadTypes.includes(item.value) || lockedUploadTypes.includes("all") ? "locked" : "open"}>
                {item.label}: {lockedUploadTypes.includes(item.value) || lockedUploadTypes.includes("all") ? "Locked" : "Open"}
              </span>
            ))}
          </div>

          {(error || notice) && (
            <div className={`report-message ${error ? "error" : "success"}`}>
              {error ? <AlertCircle size={18} /> : <CheckCircle2 size={18} />}
              <div><strong>{error ? "Please check the report" : /excel|applied/i.test(notice) ? "Import staged" : "Saved"}</strong><p>{error || notice}</p></div>
              <button onClick={() => { setError(""); setNotice(""); }}><X size={16} /></button>
            </div>
          )}

          {pendingProductionImport && !importPreview && (
            <div className="production-import-next">
              <div>
                <strong>{pendingProductionImport.categoryName} Production rows are ready</strong>
                <span>Report date: {pendingProductionImport.reportDate}. Save this category's draft, then review the other category.</span>
              </div>
              <button
                className="secondary-action"
                type="button"
                disabled={loading || isImporting}
                onClick={openPendingProductionImport}
              >
                Open {pendingProductionImport.categoryName} report
              </button>
            </div>
          )}

          {importPreview && (
            <section className="excel-review" aria-label="FATP Excel import review">
              <div className="excel-review-head">
                <div>
                  <span>{selectedUploadType.label.toUpperCase()} EXCEL PREVIEW</span>
                  <h2>Review imported rows</h2>
                  <p>Previewing {importPreview.rows.length} source row(s) for {selectedUploadType.label}. Applying only stages values in this form; it does not save or submit.</p>
                </div>
                <button className="secondary-action" type="button" onClick={cancelImportPreview}>Cancel preview</button>
              </div>
              {importPreview.errors?.map((message) => <div className="excel-date-warning" key={message}>{message}</div>)}
              {importPreview.reportDate !== reportDate && (
                <>
                  <div className="excel-date-warning">Changing the report date reloads its saved values. Save any current draft edits before continuing.</div>
                  <label className="excel-date-confirm">
                    <input
                      type="checkbox"
                      checked={importDateConfirmed}
                      onChange={(event) => {
                        const confirmed = event.target.checked;
                        setImportDateConfirmed(confirmed);
                        if (confirmed) {
                          loadSequence.current += 1;
                          setReportDate(importPreview.reportDate);
                        }
                      }}
                    />
                    <span>Confirm changing this report to the workbook date, {importPreview.reportDate}.</span>
                  </label>
                </>
              )}
              {importPreview.reportDate === reportDate && loading && (
                <div className="excel-date-warning">Loading saved reports and plan data for {reportDate} before these rows can be applied…</div>
              )}
              <p className="excel-field-note">Production workbook values are staged only for columns present in that workbook; other report sections remain unchanged.</p>
              <div className="excel-review-scroll">
                <table className="excel-review-table">
                  <thead><tr><th>Use</th><th>Source row</th><th>Original Excel values</th><th>Map to active line</th><th>Map to brand model</th><th>Values to stage</th><th>Review status</th></tr></thead>
                  <tbody>
                    {importPreview.rows.map((row, index) => {
                      const previewValues = { ...(row.form || {}), ...(row.form?.nonNumericValues || {}) };
                      const selectedLineId = Number(row.selectedLineId);
                      const selectedLine = lineData[selectedLineId];
                      const modelOptions = selectedLine?.models || [];
                      const customerMatches = isUnspecifiedCustomer(row.customer)
                        || normalizeCustomerName(row.customer) === normalizeCustomerName(customer?.customerName);
                      const canReview = Boolean(row.form) && Boolean(customerMatches);
                      const mappingReady = Boolean(selectedLine)
                        && selectedLine.isActive !== false
                        && modelOptions.some((model) => Number(model.modelId) === Number(row.selectedModelId));
                      const lockedEntry = selectedLine?.entries?.find(
                        (entry) => Number(entry.modelId) === Number(row.selectedModelId)
                          && ["SUBMITTED", "APPROVED"].includes(entry.status)
                      );
                      const statusLabel = lockedEntry
                        ? "Locked report"
                        : row.status === "matched"
                          ? "Matched"
                          : row.status === "duplicate"
                            ? "Duplicate — select at most one"
                            : row.status === "invalid"
                                ? "Invalid cells omitted"
                              : mappingReady
                                ? "Mapped — review"
                                : "Unmatched";
                      const lineSelect = (
                        <select
                          value={row.selectedLineId}
                          disabled={!canReview || !row.include}
                          onChange={(event) => {
                            const lineId = event.target.value;
                            const nextModels = lineData[Number(lineId)]?.models || [];
                            const retainsModel = nextModels.some((model) => String(model.modelId) === row.selectedModelId);
                            updateImportRow(index, {
                              selectedLineId: lineId,
                              selectedModelId: retainsModel ? row.selectedModelId : "",
                            });
                          }}
                        >
                          <option value="">Select line…</option>
                          {lines.filter((line) => lineData[line.lineId]?.isActive !== false).map((line) => <option key={line.lineId} value={line.lineId}>{line.lineName} · {line.sapLocation || "No SAP location"}</option>)}
                        </select>
                      );
                      return (
                        <tr key={`${row.rowNumber}-${index}`} className={`excel-row-${row.status}`}>
                          <td><input type="checkbox" aria-label={`Include Excel row ${row.rowNumber}`} checked={row.include} disabled={!canReview} onChange={(event) => updateImportRow(index, { include: event.target.checked })} /></td>
                          <td>{row.rowNumber}</td>
                          <td><strong>{row.lineNo || "—"}</strong><span>SAP: {row.sapLocation || "—"} · Brand: {row.customer || "—"}</span><span>Model: {row.modelName || "—"}</span></td>
                          <td>{lineSelect}</td>
                          <td>
                            <select
                              value={row.selectedModelId}
                              disabled={!canReview || !row.include || !selectedLine}
                              onChange={(event) => updateImportRow(index, { selectedModelId: event.target.value })}
                            >
                              <option value="">Select model…</option>
                              {modelOptions.map((model) => <option key={model.modelId} value={model.modelId}>{model.modelName}</option>)}
                            </select>
                          </td>
                          <td><div className="excel-staged-values">
                            {IMPORT_PREVIEW_FIELDS.filter(([, key]) => {
                              const allowed = fieldsForUploadType(uploadType);
                              return (!allowed || allowed.has(key)) && previewValues[key] !== null && previewValues[key] !== undefined && previewValues[key] !== "";
                            })
                              .map(([label, key]) => <span key={key}><b>{label}:</b> {String(previewValues[key])}</span>)}
                            {row.status === "invalid" && <span className="excel-invalid-note">Invalid cells are omitted; all other displayed values are staged.</span>}
                          </div></td>
                          <td><strong>{statusLabel}</strong>{row.reason && <span>{row.reason}</span>}</td>
                        </tr>
                      );
                    })}
                  </tbody>
                </table>
              </div>
              <div className="excel-review-actions">
                <span>Included rows are staged in the form only. Use the existing Save Draft or Submit actions after reviewing.</span>
                <button className="primary-action" type="button" disabled={isImporting || loading || !reportLoaded || importPreview.reportDate !== reportDate} onClick={applyReviewedImport}>Apply reviewed rows to form</button>
              </div>
            </section>
          )}

          {loading ? (
            <div className="report-message info"><Loader2 size={18} className="spin" /><div><strong>Loading {category?.categoryName || "daily"} report</strong><p>Loading lines, models, saved values and monthly KPI plans…</p></div></div>
          ) : (
            <section className="matrix-shell">
              <div className="matrix-validation-legend" aria-label="Field color guide">
                <span><i className="empty" />Not entered</span>
                <span><i className="valid" />Valid</span>
                <span><i className="invalid" />Check value; use “Mark OK” to confirm</span>
                <span><i className="target" />Plan / target</span>
              </div>
              <div className="matrix-scroll" ref={matrixScrollRef}>
                <MatrixTable
                  groups={reportGroups}
                  lines={lines}
                  lineData={lineData}
                  onChange={updateEntry}
                  onAddModel={addModel}
                  onRemoveModel={removeModel}
                  onDeleteDraft={deleteDraft}
                  onToggleLine={toggleLineStatus}
                  busy={busy}
                  loading={loading}
                  lockedUploadTypes={lockedUploadTypes}
                  onValidationOverride={setValidationOverride}
                />
              </div>

              <div className="matrix-add-model-bar">
                {lines.map((line) => {
                  const data = lineData[line.lineId] || { entries: [], models: [] };
                  const available = (data.models || []).filter((m) => !data.entries.some((e) => String(e.modelId) === String(m.modelId)));
                  if (!available.length) return null;
                  return <div className="line-add-model" key={line.lineId}><strong>{line.lineName}</strong><AddModelSelect models={available} onSelect={(id) => addModel(line.lineId, id)} disabled={!data.isActive} /></div>;
                })}
              </div>

              <div className="matrix-actions">
                <div className="action-groups">
                  {lines.map((line) => {
                    const data = lineData[line.lineId] || { entries: [] };
                    if (!data.entries.length) return null;
                    return <div className="line-action-group" key={line.lineId}><strong>{line.lineName}</strong>
                      <button className="secondary-action" disabled={busy === `save-${line.lineId}` || data.isActive === false} onClick={() => saveLine(line.lineId, false)}>{busy === `save-${line.lineId}` ? <Loader2 size={14} className="spin" /> : "Save Draft"}</button>
                      <button className="primary-action" disabled={busy === `submit-${line.lineId}` || data.isActive === false} onClick={() => saveLine(line.lineId, true)}>{busy === `submit-${line.lineId}` ? <Loader2 size={14} className="spin" /> : <><Send size={14} /> Submit</>}</button>
                    </div>;
                  })}
                </div>
              </div>
            </section>
          )}
        </div>
      </main>
    </ContributorLayout>
  );
}

function AddModelSelect({ models, onSelect, disabled }) {
  return <label className="add-model-control"><Plus size={13} /><select value="" onChange={(e) => onSelect(e.target.value)} disabled={disabled}><option value="">Add model</option>{models.map((model) => <option key={model.modelId} value={model.modelId}>{model.modelName}</option>)}</select></label>;
}

function MatrixTable({ groups, lines, lineData, onChange, onAddModel, onRemoveModel, onDeleteDraft, onToggleLine, busy, loading, lockedUploadTypes, onValidationOverride }) {
  const columns = lines.flatMap((line) => {
    const entries = lineData[line.lineId]?.entries || [];
    return entries.length
      ? entries.map((entry) => ({ lineId: line.lineId, entry }))
      : [{ lineId: line.lineId, entry: null }];
  });
  const tableWidth = 210 + columns.length * 300;
  return <table className="daily-matrix" style={{ "--matrix-width": `${tableWidth}px` }}>
    <colgroup>
      <col className="matrix-category-column" />
      {columns.map(({ entry }, index) => (
        <col
          className="matrix-model-column"
          key={`${entry?.entryId || "empty"}-${index}`}
        />
      ))}
    </colgroup>
    <thead>
      <tr className="line-header-row">
        <th className="parameter-head sticky-col" rowSpan="2">CATEGORY / KPI</th>
        {lines.map((line) => {
            const data = lineData[line.lineId] || { entries: [], isActive: true };
            const entryCount = Math.max(data.entries.length, 1);
            const hasSubmittedEntries = data.entries.some((entry) => ["SUBMITTED", "APPROVED"].includes(entry.status));
            return <th key={line.lineId} className={`line-group-head ${data.isActive === false ? "is-line-inactive" : ""}`} colSpan={entryCount}>
              <div className="line-head-inner">
                <div><strong>{line.lineName}</strong><span>{line.sapLocation || "SAP location not set"}</span></div>
                <button
                  type="button"
                  className="matrix-line-toggle"
                  aria-label={`${data.isActive === false ? "Activate" : "Deactivate"} ${line.lineName}`}
                  title={hasSubmittedEntries ? "Submitted or approved reports prevent line deactivation." : `${data.isActive === false ? "Activate" : "Deactivate"} this line for the selected report date`}
                  disabled={busy === `line-status-${line.lineId}` || loading || (data.isActive !== false && hasSubmittedEntries)}
                  onClick={() => onToggleLine(line)}
                >
                  {busy === `line-status-${line.lineId}`
                    ? <Loader2 size={15} className="spin" />
                    : data.isActive === false ? <ToggleLeft size={18} /> : <ToggleRight size={18} />}
                </button>
              </div>
            </th>;
        })}
      </tr>
      <tr className="model-header-row">
        {lines.map((line) => {
            const data = lineData[line.lineId] || { entries: [], models: [], isActive: true };
            if (!data.entries.length)
              return <th key={line.lineId} className="model-head empty-model-head">
                <AddModelSelect models={data.models || []} onSelect={(id) => onAddModel(line.lineId, id)} disabled={data.isActive === false} />
              </th>;
            return data.entries.map((entry) => (
              <th key={entry.entryId} className="model-head" data-entry-id={entry.entryId}>
                <div className="model-head-content">
                  <div><span>MODEL</span><strong>{entry.modelName}</strong></div>
                  {entry.rejectionReason && ["REJECTED", "DRAFT"].includes(entry.status) && <small className="review-general-comment">Reviewer summary: {entry.rejectionReason}</small>}
                  {entry.planStatus === "loading" && <small className="plan-status loading"><Loader2 size={11} className="spin" /> Loading KPI plan…</small>}
                  {entry.planStatus === "missing" && <small className="plan-status missing" title="No monthly plan exists for this plant, customer, category, line, model and month.">{entry.isNew ? "No monthly plan; targets left blank." : "No active monthly plan; saved targets are preserved."}</small>}
                  {entry.planStatus === "error" && <small className="plan-status error" title={entry.planError}>KPI plan unavailable; daily report remains editable.</small>}
                  {entry.planStatus === "ambiguous" && <small className="plan-status ambiguous" title={entry.planError}>Multiple monthly plans match; targets not prefilled.</small>}
                  {entry.isNew
                    ? <button onClick={() => onRemoveModel(line.lineId, entry.entryId)} title="Remove model"><Trash2 size={13} /></button>
                    : ["DRAFT", "REJECTED"].includes(entry.status) && <button onClick={() => onDeleteDraft(line.lineId, entry)} disabled={busy === `delete-${entry.entryId}`} title="Delete saved draft" aria-label={`Delete saved draft for ${entry.modelName}`}><Trash2 size={13} /></button>}
                </div>
              </th>
            ));
        })}
      </tr>
    </thead>
    <tbody>
      {groups.map((group) => (
        <MatrixGroup key={group.title} group={group} columns={columns} lineData={lineData} onChange={onChange} lockedUploadTypes={lockedUploadTypes} onValidationOverride={onValidationOverride} />
      ))}
    </tbody>
  </table>;
}

function MatrixGroup({ group, columns, lineData, onChange, lockedUploadTypes, onValidationOverride }) {
  return <>
    <tr className="group-row">
      <th className="sticky-col group-label">{group.title}</th>
      <td className="group-spacer" colSpan={Math.max(columns.length, 1)} />
    </tr>
    {group.rows.map((row) => <tr key={row.label} className={row.fields.some(({ key }) => key === "issueDescription") ? "issue-row" : ""}>
      <th className="sticky-col parameter-label"><span>{row.label}</span></th>
      {columns.map(({ lineId, entry }) => {
        if (!entry) return <td className="matrix-cell empty-matrix-cell" key={`${lineId}-${row.label}`} />;
        return <td className="matrix-cell" key={`${row.label}-${entry.entryId}`}>
          <div className="matrix-values-grid">
            {row.fields.map(({ key, label }) => {
              const disabled = isFieldLocked(key, lockedUploadTypes)
                || entry.status === "APPROVED"
                || entry.status === "SUBMITTED"
                || lineData[lineId]?.isActive === false;
              const validation = getFieldValidation(entry.form, key);
              const validationAccepted = Boolean(entry.validationOverrides?.[key]);
              const validationClass = validationAccepted ? "validation-accepted" : `validation-${validation.status}`;
              return <label className={`matrix-value-field ${key === "issueDescription" ? "matrix-value-wide" : ""} ${validationClass} ${entry.annotations?.some((item) => item.fieldKey === key && item.severity === "RED" && !item.resolvedAt) ? "review-field-red" : ""} ${entry.annotations?.some((item) => item.fieldKey === key && item.severity === "YELLOW" && !item.resolvedAt) ? "review-field-yellow" : ""}`} key={key}>
                <span>{label}</span>
                {key === "issueDescription"
                  ? <textarea aria-label={`${row.label} ${label}`} value={entry.form?.[key] ?? ""} disabled={disabled} placeholder="Issue / observation" onChange={(event) => onChange(lineId, entry.entryId, key, event.target.value)} />
                  : <input aria-label={`${row.label} ${label}`} type="text" inputMode="decimal" autoComplete="off" aria-invalid={!validationAccepted && validation.status === "invalid"} title={!validationAccepted && validation.status === "invalid" ? validation.message : undefined} value={entry.form?.[key] ?? ""} disabled={disabled} placeholder="Number, N/P or N/A" onChange={(event) => onChange(lineId, entry.entryId, key, event.target.value.toUpperCase())} />}
                {validation.status === "invalid" && (
                  <small className={`validation-feedback ${validationAccepted ? "accepted" : "invalid"}`}>
                    {validationAccepted ? `Marked OK by contributor: ${validation.message}` : validation.message}
                    <button
                      type="button"
                      disabled={disabled}
                      aria-label={`${validationAccepted ? "Undo OK confirmation for" : "Mark as OK"} ${row.label} ${label}`}
                      onClick={() => onValidationOverride(lineId, entry.entryId, key, !validationAccepted)}
                    >
                      {validationAccepted ? "Undo" : "Mark OK"}
                    </button>
                  </small>
                )}
                {(entry.annotations || []).filter((annotation) => annotation.fieldKey === key && !annotation.resolvedAt).map((annotation) => <small className={`review-field-note ${annotation.severity.toLowerCase()}`} key={annotation.annotationId}><strong>{annotation.severity === "RED" ? "Required correction" : "Verify / clarify"}:</strong> {annotation.comment}</small>)}
                {(entry.annotations || []).filter((annotation) => annotation.fieldKey === key && annotation.resolvedAt).length > 0 && <details className="review-field-history"><summary>Previous review ({entry.annotations.filter((annotation) => annotation.fieldKey === key && annotation.resolvedAt).length})</summary>{entry.annotations.filter((annotation) => annotation.fieldKey === key && annotation.resolvedAt).map((annotation) => <small key={annotation.annotationId}>{annotation.severity}: {annotation.comment} · submitted value: {annotation.correctedValue ?? "unchanged"}</small>)}</details>}
              </label>;
            })}
          </div>
        </td>;
      })}
    </tr>)}
  </>;
}
