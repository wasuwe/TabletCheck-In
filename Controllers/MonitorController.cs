using System;
using System.Collections.Generic;
using System.Linq;
using System.Web.Mvc;
using TabletCheckIn.Repositories;
using TabletCheckIn.Models;
using TabletCheckIn.Utility;

namespace TabletCheckIn.Controllers
{
    public class MonitorController : Controller
    {
        private readonly MonitorRepository _monitorRepo = new MonitorRepository();

        [AllowAnonymous]
        public ActionResult Index()
        {
            ViewBag.UserDept = Session["Department"]?.ToString() ?? "";
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
        [AllowAnonymous]
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
        [AllowAnonymous]
        public JsonResult GetMonitorData(string date, string search, string dept)
        {
            try
            {
                DateTime filterDate = string.IsNullOrEmpty(date) ? DateTime.Now.Date : DateTime.Parse(date).Date;
                DateTime now = DateTime.Now;
                string currentSlot = GetCurrentSlot(now);

                string currentUser = Session["Username"]?.ToString();
                string userDeptSession = Session["UserDept"]?.ToString() ?? "";

                var reportRepo = new ReportRepository();
                var (isHoliday, holidayName, isYestHoliday) = GetHolidayInfo(filterDate, reportRepo);

                var devices = _monitorRepo.GetDevicesList(dept, search, currentUser, userDeptSession);
                var logs = new List<LogRow>();

                if (devices.Count == 0)
                    return Json(new { list = devices, logs = logs, summary = new { total_devices = 0 } }, JsonRequestBehavior.AllowGet);

                var assetNos = devices.Select(d => d.asset_no).ToArray();
                var logMapToday = _monitorRepo.LoadLogsByBusinessDate(assetNos, filterDate);

                foreach (var kv in logMapToday.Values)
                    logs.Add(new LogRow { asset_no = kv.asset_no, checkin_shift = kv.checkin_shift, check_ts = kv.check_ts, status = kv.status, host_name = kv.host_name });

                var cfgMapToday = _monitorRepo.LoadEffectiveConfigMap(assetNos, filterDate);
                foreach (var d in devices)
                {
                    if (cfgMapToday.TryGetValue(d.asset_no, out var cfg))
                    {
                        d.check_morn = cfg.checkMorn;
                        d.check_night = cfg.checkNight;
                    }
                }

                var today = CalculateTodaySummary(devices, logMapToday, now, filterDate, isHoliday);

                DateTime yDate = filterDate.AddDays(-1);
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
                        if (lg.status == "Delay") yest_delay++; else yest_ok++;
                    }
                    else if (!isYestHoliday)
                    {
                        yest_miss++;
                        d.missed_yesterday_last = true;
                    }
                }

                var summary = new MonitorSummary
                {
                    total_devices = devices.Count,
                    online_count = devices.Count(d => d.is_online),
                    offline_count = devices.Count(d => !d.is_online),
                    morn_ok = today.morn_ok,
                    morn_delay = today.morn_delay,
                    morn_miss = today.morn_miss,
                    morn_expected = today.morn_exp,
                    night_ok = today.night_ok,
                    night_delay = today.night_delay,
                    night_miss = today.night_miss,
                    night_expected = today.night_exp,
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
                    server_hour = now.Hour,
                    isHoliday = isHoliday,
                    holidayName = holidayName
                }, JsonRequestBehavior.AllowGet);
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[System Error] : {ex.Message}");
                return Json(new { status = "error", message = "ระบบขัดข้องชั่วคราว ไม่สามารถดึงข้อมูลได้" }, JsonRequestBehavior.AllowGet);
            }
        }

        [HttpGet]
        [AppAuthorize]
        public JsonResult GetHistory(string asset, int offset)
        {
            var history = _monitorRepo.GetHistoryLogs(asset, offset);
            return Json(history, JsonRequestBehavior.AllowGet);
        }

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

        private (bool isHoliday, string holidayName, bool isYestHoliday) GetHolidayInfo(DateTime filterDate, ReportRepository reportRepo)
        {
            var holidays = reportRepo.GetHolidaysFromApi(filterDate.Year, filterDate.Month);
            holidays.TryGetValue(filterDate.Day.ToString(), out string hType);
            bool isHoliday = !string.IsNullOrEmpty(hType) && (hType.ToUpper() == "T" || hType.ToUpper() == "H");
            string holidayName = isHoliday
                ? (hType.ToUpper() == "T" ? "Traditional Holiday (วันหยุดประเพณี)" : "Company Holiday (วันหยุดบริษัท)")
                : "";

            DateTime yDate = filterDate.AddDays(-1);
            var yHolidays = yDate.Month == filterDate.Month
                ? holidays
                : reportRepo.GetHolidaysFromApi(yDate.Year, yDate.Month);
            yHolidays.TryGetValue(yDate.Day.ToString(), out string yhType);
            bool isYestHoliday = !string.IsNullOrEmpty(yhType) && (yhType.ToUpper() == "T" || yhType.ToUpper() == "H");

            return (isHoliday, holidayName, isYestHoliday);
        }

        private (int morn_ok, int morn_delay, int morn_miss, int morn_exp, int night_ok, int night_delay, int night_miss, int night_exp)
            CalculateTodaySummary(List<DeviceRow> devices, Dictionary<string, LogRow> logMap, DateTime now, DateTime filterDate, bool isHoliday)
        {
            int morn_exp = 0, morn_ok = 0, morn_delay = 0;
            int night_exp = 0, night_ok = 0, night_delay = 0;

            foreach (var d in devices)
            {
                if (d.check_morn)
                {
                    morn_exp++;
                    if (logMap.TryGetValue($"{d.asset_no}||08:00-12:00", out var lg))
                    {
                        if (lg.status == "Delay") morn_delay++; else morn_ok++;
                    }
                }
                if (d.check_night)
                {
                    night_exp++;
                    if (logMap.TryGetValue($"{d.asset_no}||20:00-00:00", out var lg))
                    {
                        if (lg.status == "Delay") night_delay++; else night_ok++;
                    }
                }
            }

            int serverHour = now.Hour;
            int morn_miss = (filterDate.Date < now.Date || (filterDate.Date == now.Date && serverHour >= 12))
                ? Math.Max(0, morn_exp - (morn_ok + morn_delay)) : 0;
            DateTime nightShiftDeadline = filterDate.Date.AddDays(1).AddHours(8);
            int night_miss = (now >= nightShiftDeadline)
                ? Math.Max(0, night_exp - (night_ok + night_delay)) : 0;

            if (isHoliday)
            {
                morn_miss = 0;
                morn_exp = morn_ok + morn_delay;
                night_miss = 0;
                night_exp = night_ok + night_delay;
            }

            return (morn_ok, morn_delay, morn_miss, morn_exp, night_ok, night_delay, night_miss, night_exp);
        }

        private string GetRequiredSlotForCompliance(bool checkMorn, bool checkNight)
        {
            if (checkNight) return "20:00-00:00";
            if (checkMorn) return "08:00-12:00";
            return "";
        }

        private string GetCurrentSlot(DateTime now)
        {
            int h = now.Hour;
            if (h >= 8 && h < 20) return "08:00-12:00";
            return "20:00-00:00";
        }
    }
}
