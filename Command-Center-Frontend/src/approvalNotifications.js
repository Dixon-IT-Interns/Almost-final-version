export function groupApproverNotificationsByDate(notifications) {
  const dates = new Map();
  for (const item of notifications) {
    const reportDate = String(item.reportDate || "").slice(0, 10) || "unknown";
    if (!dates.has(reportDate)) dates.set(reportDate, new Set());
    const reportKey = item.submissionBatchId || `legacy-${item.dailyReportId}`;
    dates.get(reportDate).add(reportKey);
  }

  return [...dates.entries()]
    .sort(([dateA], [dateB]) => dateB.localeCompare(dateA))
    .map(([reportDate, reports]) => ({
      reportDate,
      count: reports.size,
    }));
}
