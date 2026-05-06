using System;
using System.Collections.Generic;
using System.Linq;
using System.Web.Mvc;
using TabletCheckIn.Repositories;
using TabletCheckIn.Models;

namespace TabletCheckIn.Controllers
{
    [AllowAnonymous]
    public class MonitorController : Controller
    {
        private readonly MonitorRepository _monitorRepo = new MonitorRepository();

        public ActionResult Index()
        {
            ViewBag.UserDept = Session["Department"]?.ToString() ?? "";
            // 🌟 ไม่ต้องสืบหา IP จาก Server แล้ว ปล่อยให้หน้าจอจัดการผ่าน LocalStorage
            return View();
        }

        [AllowAnonymous]
        public ActionResult TestErrorUI()
        {
            return View("~/Views/Shared/Error.cshtml");
        }

        [AllowAnonymous]
        public ActionResult TriggerFakeError()
        {
            throw new Exception("นี่คือการจำลองข้อผิดพลาด เพื่อดูว่าหน้า Error สวยงามแค่ไหน!");
        }

        [HttpGet]
        public JsonResult GetDepartments()
        {
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
                return Json(new { status = "error", message = "ระบบขัดข้องชั่วคราว ไม่สามารถดึงข้อมูลได้" }, JsonRequestBehavior.AllowGet);
            }
        }

        [HttpGet]
        public JsonResult GetMonitorData(string date, string search, string dept)
        {
            try
            {
                DateTime filterDate = string.IsNullOrEmpty(date) ? DateTime.Now.Date : DateTime.Parse(date).Date;
                DateTime now = DateTime.Now;
                int serverHour = now.Hour;
                string currentSlot = GetCurrentSlotByServerTimeWithDelay(now);

                string currentUser = Session["Username"]?.ToString();
                string userDeptSession = Session["UserDept"]?.ToString() ?? "";

                // 🌟 1. ดึงข้อมูลวันหยุดจาก API (ReportRepository)
                var reportRepo = new ReportRepository();
                var holidays = reportRepo.GetHolidaysFromApi(filterDate.Year, filterDate.Month);

                holidays.TryGetValue(filterDate.Day.ToString(), out string hType);
                // เช็คว่าเป็น T (วันหยุดประเพณี) หรือ H (วันหยุดบริษัท)
                bool isHoliday = !string.IsNullOrEmpty(hType) && (hType.ToUpper() == "T" || hType.ToUpper() == "H");
                string holidayName = isHoliday ? (hType.ToUpper() == "T" ? "Traditional Holiday (วันหยุดประเพณี)" : "Company Holiday (วันหยุดบริษัท)") : "";

                // เช็ควันหยุดของเมื่อวานด้วย (สำหรับกะดึก 20:00-00:00 ที่ข้ามวัน)
                DateTime yDate = filterDate.AddDays(-1);
                var yHolidays = yDate.Month == filterDate.Month ? holidays : reportRepo.GetHolidaysFromApi(yDate.Year, yDate.Month);
                yHolidays.TryGetValue(yDate.Day.ToString(), out string yhType);
                bool isYestHoliday = !string.IsNullOrEmpty(yhType) && (yhType.ToUpper() == "T" || yhType.ToUpper() == "H");


                // 🌟 2. ดึงข้อมูลอุปกรณ์
                var devices = _monitorRepo.GetDevicesList(dept, search, currentUser, userDeptSession);
                var logs = new List<LogRow>();

                if (devices.Count == 0)
                {
                    return Json(new { list = devices, logs = logs, summary = new { total_devices = 0 } }, JsonRequestBehavior.AllowGet);
                }

                var assetNos = devices.Select(d => d.asset_no).ToArray();
                var logMapToday = _monitorRepo.LoadLogsByBusinessDate(assetNos, filterDate);

                foreach (var kv in logMapToday.Values)
                {
                    // ส่งข้อมูล host_name กลับไปให้ UI ด้วย
                    logs.Add(new LogRow { asset_no = kv.asset_no, checkin_shift = kv.checkin_shift, check_ts = kv.check_ts, status = kv.status, host_name = kv.host_name });
                }

                var cfgMapToday = _monitorRepo.LoadEffectiveConfigMap(assetNos, filterDate);

                int morn_exp = 0, morn_ok = 0, morn_delay = 0;
                int night_exp = 0, night_ok = 0, night_delay = 0;

                foreach (var d in devices)
                {
                    bool cm = d.check_morn, cn = d.check_night;
                    if (cfgMapToday.TryGetValue(d.asset_no, out var cfg))
                    {
                        d.check_morn = cfg.checkMorn; d.check_night = cfg.checkNight;
                    }

                    if (d.check_morn)
                    {
                        morn_exp++;
                        if (logMapToday.TryGetValue($"{d.asset_no}||08:00-12:00", out var lg))
                        {
                            if (lg.status == "Delay") morn_delay++; else morn_ok++;
                        }
                    }
                    if (d.check_night)
                    {
                        night_exp++;
                        if (logMapToday.TryGetValue($"{d.asset_no}||20:00-00:00", out var lg))
                        {
                            if (lg.status == "Delay") night_delay++; else night_ok++;
                        }
                    }
                }

                int morn_miss = (filterDate.Date < now.Date || (filterDate.Date == now.Date && serverHour >= 12)) ? Math.Max(0, morn_exp - (morn_ok + morn_delay)) : 0;
                DateTime nightShiftDeadline = filterDate.Date.AddDays(1).AddHours(8);
                int night_miss = (now >= nightShiftDeadline) ? Math.Max(0, night_exp - (night_ok + night_delay)) : 0;

                // 🌟 3. ปรับยอด Miss เป็น 0 หากเป็นวันหยุด
                if (isHoliday)
                {
                    morn_miss = 0;
                    morn_exp = morn_ok + morn_delay; // ปรับเป้าให้เท่าคนที่มาทำจริง
                    night_miss = 0;
                    night_exp = night_ok + night_delay;
                }

                var logMapY = _monitorRepo.LoadLogsByBusinessDate(assetNos, yDate);
                var cfgMapY = _monitorRepo.LoadEffectiveConfigMap(assetNos, yDate);

                int yest_ok = 0, yest_delay = 0, yest_miss = 0;

                foreach (var d in devices)
                {
                    bool cm = d.check_morn, cn = d.check_night;
                    if (cfgMapY.TryGetValue(d.asset_no, out var cfg))
                    {
                        cm = cfg.checkMorn;
                        cn = cfg.checkNight;
                    }

                    string reqSlot = GetRequiredSlotForCompliance(cm, cn);
                    if (string.IsNullOrEmpty(reqSlot)) continue;

                    if (logMapY.TryGetValue($"{d.asset_no}||{reqSlot}", out var lg))
                    {
                        if (lg.status == "Delay") yest_delay++;
                        else yest_ok++;
                    }
                    else
                    {
                        // 🌟 หากเมื่อวานเป็นวันหยุด จะไม่นำมาคิด Miss ของ Yesterday
                        if (!isYestHoliday)
                        {
                            yest_miss++;
                            d.missed_yesterday_last = true;
                        }
                    }
                }

                var summary = new MonitorSummary
                {
                    total_devices = devices.Count,
                    online_count = devices.Count(d => d.is_online),
                    offline_count = devices.Count(d => !d.is_online),
                    morn_ok = morn_ok,
                    morn_delay = morn_delay,
                    morn_miss = morn_miss,
                    morn_expected = morn_exp,
                    night_ok = night_ok,
                    night_delay = night_delay,
                    night_miss = night_miss,
                    night_expected = night_exp,
                    yest_ok = yest_ok,
                    yest_delay = yest_delay,
                    yest_miss = yest_miss,
                    dept_breakdown = devices.GroupBy(x => x.dept_name ?? "").ToDictionary(g => g.Key, g => g.Count())
                };

                return Json(new
                {
                    list = devices,
                    logs = logs,
                    summary = summary,
                    currentSlot = currentSlot,
                    server_hour = serverHour,
                    isHoliday = isHoliday,       // ส่งสถานะไปให้ UI
                    holidayName = holidayName    // ส่งชื่อวันหยุดไปให้ UI
                }, JsonRequestBehavior.AllowGet);
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[System Error] : {ex.Message}");
                return Json(new { status = "error", message = "ระบบขัดข้องชั่วคราว ไม่สามารถดึงข้อมูลได้" }, JsonRequestBehavior.AllowGet);
            }
        }

        private string GetRequiredSlotForCompliance(bool checkMorn, bool checkNight)
        {
            if (checkMorn && checkNight) return "20:00-00:00";
            if (checkNight) return "20:00-00:00";
            if (checkMorn) return "08:00-12:00";
            return "";
        }

        [HttpGet]
        public JsonResult GetHistory(string asset, int offset)
        {
            var history = _monitorRepo.GetHistoryLogs(asset, offset);
            return Json(history, JsonRequestBehavior.AllowGet);
        }

        // ==========================================
        // 🌟 FORCE CHECK-IN API (อิง Host Name)
        // ==========================================

        [HttpGet]
        [AllowAnonymous]
        public JsonResult CheckHostStatus(string hostName)
        {
            try
            {
                bool isCheckedIn = _monitorRepo.VerifyHostNameCheckInStatus(hostName);
                return Json(new { isCheckedIn = isCheckedIn }, JsonRequestBehavior.AllowGet);
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[CheckHostStatus Error] : {ex.Message}");
                return Json(new { isCheckedIn = true }, JsonRequestBehavior.AllowGet);
            }
        }

        [HttpPost]
        [AllowAnonymous]
        public JsonResult SubmitCheckIn(string hostName)
        {
            try
            {
                string checkStatus = _monitorRepo.PerformForceCheckInByHostName(hostName);
                return Json(new { success = true, status = checkStatus, message = "Check-in completed." });
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[ForceCheckIn Error] : {ex.Message}");
                return Json(new { success = false, message = ex.Message });
            }
        }

        private string GetCurrentSlotByServerTimeWithDelay(DateTime now)
        {
            int h = now.Hour;
            if (h >= 8 && h < 20) return "08:00-12:00";
            if (h >= 20 || h < 8) return "20:00-00:00";
            return "";
        }
    }
}