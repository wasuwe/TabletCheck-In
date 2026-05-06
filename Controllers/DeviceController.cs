using System;
using System.Web.Mvc;
using TabletCheckIn.Repositories;
using TabletCheckIn.Models;

namespace TabletCheckIn.Controllers
{
    public class DeviceController : Controller
    {
        private readonly DeviceRepository _deviceRepo = new DeviceRepository();
        private readonly MonitorRepository _monitorRepo = new MonitorRepository(); // ยืมใช้ดึงรายชื่อแผนก

        // โหลดหน้า HTML 
        [HttpGet]
        public ActionResult Index()
        {
            // ดักผู้ใช้ที่ยังไม่ Login
            if (Session["Username"] == null)
            {
                return RedirectToAction("Index", "Auth");
            }

            ViewBag.Title = "Manage Devices";
            ViewBag.FullName = Session["FullName"]?.ToString();
            return View();
        }

        [HttpGet]
        public JsonResult GetList()
        {
            if (Session["Username"] == null)
                return Json(new { status = "error", message = "Session expired." }, JsonRequestBehavior.AllowGet);

            try
            {
                string currentUser = Session["Username"].ToString();
                string userDept = Session["UserDept"]?.ToString() ?? "";

                var list = _deviceRepo.GetList(currentUser, userDept);
                return Json(list, JsonRequestBehavior.AllowGet);
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

        [HttpPost]
        [ValidateAntiForgeryToken]
        public JsonResult Save(DeviceSaveRequest req)
        {
            if (Session["Username"] == null)
                return Json(new { status = "error", message = "Session expired." });

            try
            {
                string currentUser = Session["Username"].ToString();
                string currentName = Session["FullName"]?.ToString() ?? "";

                _deviceRepo.SaveDevice(req, currentUser, currentName);
                return Json(new { status = "success" });
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

        [HttpPost]
        [ValidateAntiForgeryToken]
        public JsonResult Delete(int del_id)
        {
            if (Session["Username"] == null)
                return Json(new { status = "error", message = "Session expired." });

            try
            {
                _deviceRepo.DeleteDevice(del_id);
                return Json(new { status = "success" });
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
        public JsonResult GetLogs(int id, int offset = 0)
        {
            var logs = _deviceRepo.GetDeviceLogs(id, offset);
            return Json(logs, JsonRequestBehavior.AllowGet);
        }
    }
}