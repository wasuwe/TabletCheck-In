using System;
using System.Linq;
using System.Web.Mvc;
using TabletCheckIn.Repositories;
using TabletCheckIn.Models;
using TabletCheckIn.Utility;
using System.IO;
using ClosedXML.Excel;
using System.Collections.Generic;

namespace TabletCheckIn.Controllers
{
    [AppAuthorize]
    public class ReportController : Controller
    {
        private readonly ReportRepository _reportRepo = new ReportRepository();
        private readonly MonitorRepository _monitorRepo = new MonitorRepository(); // ยืมดึง Dept

        [HttpGet]
        public ActionResult Index()
        {
            // ดึงแผนกของ User ปัจจุบัน (ถ้าไม่มีจะกลายเป็นค่าว่าง "")
            ViewBag.UserDept = Session["Department"]?.ToString() ?? "";
            return View();
        }

        [HttpGet]
        public JsonResult GetDepartments()
        {
            // ถ้าเป็น Guest จะให้ currentUser เป็นค่าว่าง
            string currentUser = Session["Username"]?.ToString() ?? "";
            string userDeptSession = Session["UserDept"]?.ToString() ?? "";

            try
            {
                var depts = _monitorRepo.GetDepartments(currentUser, userDeptSession);
                return Json(depts, JsonRequestBehavior.AllowGet);
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[System Error] : {ex.Message}");
                return Json(new
                {
                    status = "error",
                    message = "ระบบขัดข้องชั่วคราว ไม่สามารถดึงข้อมูลได้ กรุณาติดต่อแผนก IT"
                });
            }
        }

        [HttpGet]
        public JsonResult GetReportData(string month, string search, string dept)
        {
            try
            {
                if (string.IsNullOrEmpty(month)) month = DateTime.Now.ToString("yyyy-MM");

                DateTime selectedMonth = DateTime.Parse(month + "-01");
                int yr = selectedMonth.Year;
                int mnth = selectedMonth.Month;
                int daysInMonth = DateTime.DaysInMonth(yr, mnth);

                // ดึงข้อมูล 4 ก้อนหลัก
                var holidayMap = _reportRepo.GetHolidaysFromApi(yr, mnth);

                // 🌟 1. แทรกโค้ด Fallback ตรงนี้: ถ้า API ล่มให้สร้างวันอาทิตย์ยัดใส่ Map ส่งไปให้หน้าเว็บเลย 🌟
                if (holidayMap == null) holidayMap = new Dictionary<string, string>();
                if (holidayMap.Count == 0)
                {
                    for (int d = 1; d <= daysInMonth; d++)
                    {
                        if (new DateTime(yr, mnth, d).DayOfWeek == DayOfWeek.Sunday)
                        {
                            holidayMap.Add(d.ToString(), "H"); // H จะทำให้หน้าเว็บโชว์สัญลักษณ์ ●
                        }
                    }
                }

                var historyMap = _reportRepo.GetAllConfigHistory();
                var reportRows = _reportRepo.GetReportDevices(search, dept);
                var logs = _reportRepo.GetRawLogs(yr, mnth);

                DateTime today = DateTime.Today;

                // วนลูปสร้างหน้าตารายงาน
                foreach (var device in reportRows)
                {
                    int totalRequired = 0, totalChecked = 0;

                    for (int d = 1; d <= daysInMonth; d++)
                    {
                        string dayKey = d.ToString();
                        DateTime currentDate = new DateTime(yr, mnth, d);
                        var dayStatus = new DayStatus();

                        string holidayType = holidayMap.ContainsKey(dayKey) ? holidayMap[dayKey] : "";
                        bool isHoliday = (holidayType == "T" || holidayType == "H");

                        // 🌟 FALLBACK: ถ้า API ล่ม (ไม่มีข้อมูลใน Map เลย) ให้ถือว่า "วันอาทิตย์" เป็นวันหยุดอัตโนมัติ
                        if (holidayMap.Count == 0 && currentDate.DayOfWeek == DayOfWeek.Sunday)
                        {
                            isHoliday = true;
                            holidayType = "H"; // กำหนดให้เป็นวันหยุดประจำสัปดาห์ (โชว์จุดสีแดงในตาราง)
                        }

                        bool targetMorn = device.check_morn, targetNight = device.check_night;
                        string effectiveStatus = device.status;

                        if (historyMap.ContainsKey(device.asset_no))
                        {
                            var hist = historyMap[device.asset_no].Where(h => h.EffectiveDate.Date <= currentDate.Date).OrderByDescending(h => h.EffectiveDate).FirstOrDefault();
                            if (hist != null)
                            {
                                targetMorn = hist.CheckMorn; targetNight = hist.CheckNight; effectiveStatus = hist.Status;
                            }
                        }

                        if (device.reg_date_obj.HasValue && currentDate < device.reg_date_obj.Value.Date)
                        {
                            dayStatus.morn = new SlotResult { status = "NA" };
                            dayStatus.night = new SlotResult { status = "NA" };
                        }
                        else if (effectiveStatus.Equals("Stock", StringComparison.OrdinalIgnoreCase))
                        {
                            string st = (currentDate > today) ? "Future" : "Stock";
                            dayStatus.morn = new SlotResult { status = st }; dayStatus.night = new SlotResult { status = st };
                        }
                        else
                        {
                            dayStatus.morn = CalculateSlotStatus(device.asset_no, "08:00-12:00", logs, currentDate, today, targetMorn, "morn");
                            dayStatus.night = CalculateSlotStatus(device.asset_no, "20:00-00:00", logs, currentDate, today, targetNight, "night");
                        }

                        // 🌟 แก้ไข: แม้จะเป็นวันหยุด (isHoliday) แต่ถ้าเขามา Check-in (สถานะเป็น OK หรือ Delay) 
                        // ให้คงสถานะนั้นไว้ (เอาไปนับคะแนนเป็นโบนัสด้วย) แต่ถ้าไม่มา (Missed/Future/Stock/Wait) ให้ข้ามเป็น N/A ไป
                        if (isHoliday)
                        {
                            if (dayStatus.morn.status != "OK" && dayStatus.morn.status != "Delay") dayStatus.morn.status = "NA";
                            if (dayStatus.night.status != "OK" && dayStatus.night.status != "Delay") dayStatus.night.status = "NA";
                        }

                        device.days.Add(dayKey, dayStatus);

                        if (!isHoliday)
                        {
                            Action<string> countStatus = (status) => {
                                if (status == "OK") { device.count_ok++; totalRequired++; totalChecked++; }
                                else if (status == "Delay") { device.count_delay++; totalRequired++; totalChecked++; }
                                else if (status == "Missed") { device.count_missed++; totalRequired++; }
                            };
                            countStatus(dayStatus.morn.status); countStatus(dayStatus.night.status);
                        }
                    }

                    device.score = totalRequired > 0 ? Math.Round(((double)totalChecked / (double)totalRequired) * 100.0, 1) : 0;
                }

                return new JsonResult
                {
                    Data = new { days_in_month = daysInMonth, rows = reportRows, holidays = holidayMap },
                    JsonRequestBehavior = JsonRequestBehavior.AllowGet,
                    MaxJsonLength = int.MaxValue
                };
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[System Error] : {ex.Message}");
                return Json(new { status = "error", message = "ระบบขัดข้องชั่วคราว ไม่สามารถดึงข้อมูลได้ กรุณาติดต่อแผนก IT" }, JsonRequestBehavior.AllowGet);
            }
        }

        private SlotResult CalculateSlotStatus(string asset, string slotName, System.Collections.Generic.List<RawLogData> allLogs, DateTime targetDate, DateTime today, bool isConfigRequired, string slotType)
        {
            if (!isConfigRequired) return new SlotResult { status = "NA" };
            var log = allLogs.FirstOrDefault(l => l.asset_no == asset && l.checkin_shift == slotName && l.business_date == targetDate);

            if (log != null)
            {
                bool isDelay = false; int h = log.checkin_time.Hour;
                if (slotType == "morn" && h >= 12) isDelay = true;
                if (slotType == "night" && (h >= 0 && h < 8)) isDelay = true;

                // คืนค่าเวลา (Time) กลับไปด้วยเพื่อนำไปแสดงผล
                return new SlotResult { status = isDelay ? "Delay" : "OK", time = log.checkin_time.ToString("HH:mm") };
            }
            else
            {
                if (targetDate < today) return new SlotResult { status = "Missed" };
                else if (targetDate == today)
                {
                    int currentHour = DateTime.Now.Hour;
                    if (slotType == "morn") { if (currentHour >= 12) return new SlotResult { status = "Missed" }; if (currentHour >= 8) return new SlotResult { status = "Wait" }; }
                    else { if (currentHour >= 20) return new SlotResult { status = "Wait" }; }
                    return new SlotResult { status = "Future" };
                }
                else return new SlotResult { status = "Future" };
            }
        }

        [HttpGet]
        public ActionResult ExportExcel(string month, string dept)
        {
            try
            {
                if (string.IsNullOrEmpty(month)) month = DateTime.Now.ToString("yyyy-MM");

                DateTime selectedMonth = DateTime.Parse(month + "-01");
                int yr = selectedMonth.Year;
                int mnth = selectedMonth.Month;
                int daysInMonth = DateTime.DaysInMonth(yr, mnth);

                var holidayMap = _reportRepo.GetHolidaysFromApi(yr, mnth);

                // 🌟 2. แทรกโค้ด Fallback สำหรับ Excel 🌟
                if (holidayMap == null) holidayMap = new Dictionary<string, string>();
                if (holidayMap.Count == 0)
                {
                    for (int d = 1; d <= daysInMonth; d++)
                    {
                        if (new DateTime(yr, mnth, d).DayOfWeek == DayOfWeek.Sunday)
                        {
                            holidayMap.Add(d.ToString(), "H");
                        }
                    }
                }

                var historyMap = _reportRepo.GetAllConfigHistory();
                var reportRows = _reportRepo.GetReportDevices("", dept);
                var logs = _reportRepo.GetRawLogs(yr, mnth);
                DateTime today = DateTime.Today;

                using (var wb = new XLWorkbook())
                {
                    var ws = wb.Worksheets.Add("CheckIn_Report_" + month);

                    // --- 1. สร้างหัวตาราง (Headers) ---
                    ws.Cell(1, 1).Value = "IT Asset";
                    ws.Cell(1, 2).Value = "Owner";
                    ws.Cell(1, 3).Value = "Department";
                    ws.Cell(1, 4).Value = "Score (%)";
                    ws.Cell(1, 5).Value = "Total OK";
                    ws.Cell(1, 6).Value = "Total Delay";
                    ws.Cell(1, 7).Value = "Total Missed";

                    // สร้างคอลัมน์สำหรับแต่ละวันและแต่ละกะ
                    int col = 8;
                    for (int d = 1; d <= daysInMonth; d++)
                    {
                        ws.Cell(1, col).Value = $"{d}_Morn";
                        ws.Cell(1, col + 1).Value = $"{d}_Night";
                        col += 2;
                    }

                    // --- 2. วนลูปใส่ข้อมูล (ใช้ Logic เดียวกับหน้าเว็บเป๊ะๆ) ---
                    int row = 2;
                    foreach (var device in reportRows)
                    {
                        int totalRequired = 0, totalChecked = 0;
                        int countOk = 0, countDelay = 0, countMissed = 0;

                        ws.Cell(row, 1).Value = device.asset_no;
                        ws.Cell(row, 2).Value = device.owner_name;
                        ws.Cell(row, 3).Value = device.dept_name;

                        col = 8;
                        for (int d = 1; d <= daysInMonth; d++)
                        {
                            DateTime currentDate = new DateTime(yr, mnth, d);
                            string dayKey = d.ToString();
                            string holidayType = holidayMap.ContainsKey(dayKey) ? holidayMap[dayKey] : "";
                            bool isHoliday = (holidayType == "T" || holidayType == "H");

                            // 🌟 FALLBACK: ถ้า API ล่ม ให้ใช้วันอาทิตย์เป็นวันหยุด
                            if (holidayMap.Count == 0 && currentDate.DayOfWeek == DayOfWeek.Sunday)
                            {
                                isHoliday = true;
                            }

                            bool targetMorn = device.check_morn, targetNight = device.check_night;
                            string effectiveStatus = device.status;

                            if (historyMap.ContainsKey(device.asset_no))
                            {
                                var hist = historyMap[device.asset_no].Where(h => h.EffectiveDate.Date <= currentDate.Date).OrderByDescending(h => h.EffectiveDate).FirstOrDefault();
                                if (hist != null)
                                {
                                    targetMorn = hist.CheckMorn; targetNight = hist.CheckNight; effectiveStatus = hist.Status;
                                }
                            }

                            SlotResult mornRes = new SlotResult { status = "Future" };
                            SlotResult nightRes = new SlotResult { status = "Future" };

                            if (device.reg_date_obj.HasValue && currentDate < device.reg_date_obj.Value.Date)
                            {
                                mornRes.status = "NA"; nightRes.status = "NA";
                            }
                            else if (effectiveStatus.Equals("Stock", StringComparison.OrdinalIgnoreCase))
                            {
                                string st = (currentDate > today) ? "Future" : "Stock";
                                mornRes.status = st; nightRes.status = st;
                            }
                            else
                            {
                                mornRes = CalculateSlotStatus(device.asset_no, "08:00-12:00", logs, currentDate, today, targetMorn, "morn");
                                nightRes = CalculateSlotStatus(device.asset_no, "20:00-00:00", logs, currentDate, today, targetNight, "night");
                            }

                            // 🌟 แก้ไข: คงสถานะการ Check-in ไว้ในวันหยุด ถ้ามี
                            if (isHoliday)
                            {
                                if (mornRes.status != "OK" && mornRes.status != "Delay") mornRes.status = "NA";
                                if (nightRes.status != "OK" && nightRes.status != "Delay") nightRes.status = "NA";
                            }

                            if (!isHoliday)
                            {
                                Action<string> countStatus = (status) => {
                                    if (status == "OK") { countOk++; totalRequired++; totalChecked++; }
                                    else if (status == "Delay") { countDelay++; totalRequired++; totalChecked++; }
                                    else if (status == "Missed") { countMissed++; totalRequired++; }
                                };
                                countStatus(mornRes.status); countStatus(nightRes.status);
                            }

                            // 🌟 เขียนสถานะพร้อมเวลาสแกนลงใน Excel (เช่น "OK [08:15]")
                            ws.Cell(row, col).Value = FormatCellForExcel(mornRes);
                            ws.Cell(row, col + 1).Value = FormatCellForExcel(nightRes);

                            col += 2;
                        }

                        // คำนวณเปอร์เซ็นต์
                        double score = totalRequired > 0 ? Math.Round(((double)totalChecked / (double)totalRequired) * 100.0, 1) : 0;

                        ws.Cell(row, 4).Value = score;
                        ws.Cell(row, 5).Value = countOk;
                        ws.Cell(row, 6).Value = countDelay;
                        ws.Cell(row, 7).Value = countMissed;

                        row++;
                    }

                    // --- 3. จัด Format และความสวยงาม (Freeze Panes / Color) ---
                    var headerRange = ws.Range(1, 1, 1, col - 1);
                    headerRange.Style.Font.Bold = true;
                    headerRange.Style.Fill.BackgroundColor = XLColor.LightGray;

                    ws.SheetView.FreezeRows(1); // แช่แข็งแถวบนสุด (Headers)
                    ws.SheetView.FreezeColumns(3); // แช่แข็ง 3 คอลัมน์แรก (Asset, Owner, Dept)

                    ws.Columns().AdjustToContents();

                    // --- 4. ส่งกลับเป็นไฟล์ ---
                    using (var stream = new MemoryStream())
                    {
                        wb.SaveAs(stream);
                        return File(stream.ToArray(), "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet", $"Tablet_CheckIn_Report_{month}.xlsx");
                    }
                }
            }
            catch (Exception ex)
            {
                return Content($"Error generating Excel: {ex.Message}");
            }
        }

        // ฟังก์ชันช่วยรวม Status กับเวลา ให้หน้าตาดูง่าย (เช่น "Delay [12:05]")
        private string FormatCellForExcel(SlotResult slot)
        {
            if (slot.status == "OK" || slot.status == "Delay")
                return $"{slot.status} {(!string.IsNullOrEmpty(slot.time) ? $"[{slot.time}]" : "")}".Trim();

            return slot.status; // NA, Missed, Future, Stock
        }
    }
}