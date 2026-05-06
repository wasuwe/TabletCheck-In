using System;
using System.Web.Mvc;
using TabletCheckIn.Repositories;
using TabletCheckIn.Models;
using TabletCheckIn.Utility;

namespace TabletCheckIn.Controllers
{
    [AppAuthorize(AllowedRoles = "Admin")]
    public class AdminController : Controller
    {
        private readonly AdminRepository _adminRepo = new AdminRepository();
        private readonly DeviceRepository _deviceRepo = new DeviceRepository();
        private readonly MonitorRepository _monitorRepo = new MonitorRepository();

        [HttpGet]
        public ActionResult Index()
        {
            ViewBag.Title = "Admin Management";
            return View();
        }

        // ================= USERS API =================

        [HttpGet]
        public JsonResult GetUsers()
        {
            return Json(_adminRepo.GetUsers(), JsonRequestBehavior.AllowGet);
        }

        [HttpPost]
        public JsonResult SaveUser(UserSaveRequest req)
        {
            if (string.IsNullOrWhiteSpace(req.username))
                return Json(new { status = "error", message = "Username is required." });

            if (!string.IsNullOrWhiteSpace(req.email) && !req.email.Trim().ToLower().EndsWith("@mail.canon"))
                return Json(new { status = "error", message = "Email must end with @mail.canon" });

            try
            {
                _adminRepo.SaveUser(req);
                bool hasEmail = !string.IsNullOrWhiteSpace(req.email);
                return Json(new { status = "success", hasEmail = hasEmail, username = req.username });
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[System Error] : {ex.Message}");
                return Json(new { status = "error", message = "ระบบขัดข้องชั่วคราว ไม่สามารถบันทึกข้อมูลได้" });
            }
        }

        [HttpPost]
        public JsonResult DeleteUser(string id)
        {
            try
            {
                _adminRepo.DeleteUser(id);
                return Json(new { status = "success" });
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[System Error] : {ex.Message}");
                return Json(new { status = "error", message = "ระบบขัดข้องชั่วคราว ไม่สามารถดึงข้อมูลได้ กรุณาติดต่อแผนก IT" });
            }
        }

        // ================= DEPTS API =================

        [HttpGet]
        public JsonResult GetDepartments()
        {
            return Json(_adminRepo.GetDepartments(), JsonRequestBehavior.AllowGet);
        }

        [HttpPost]
        public JsonResult SaveDepartment(DeptSaveRequest req)
        {
            if (string.IsNullOrWhiteSpace(req.dept_name))
                return Json(new { status = "error", message = "Department name is required." });

            try
            {
                _adminRepo.SaveDepartment(req);
                return Json(new { status = "success" });
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[System Error] : {ex.Message}");
                return Json(new { status = "error", message = "ระบบขัดข้องชั่วคราว ไม่สามารถดึงข้อมูลได้ กรุณาติดต่อแผนก IT" });
            }
        }

        [HttpPost]
        public JsonResult DeleteDepartment(int id)
        {
            try
            {
                _adminRepo.DeleteDepartment(id);
                return Json(new { status = "success" });
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[System Error] : {ex.Message}");
                return Json(new { status = "error", message = "ระบบขัดข้องชั่วคราว ไม่สามารถดึงข้อมูลได้ กรุณาติดต่อแผนก IT" });
            }
        }

        // ================= SEND EMAIL SETUP LINK =================

        [HttpPost]
        public JsonResult SendSetupLink(string username)
        {
            try
            {
                string email = _adminRepo.GetUserEmail(username);
                if (string.IsNullOrEmpty(email))
                    return Json(new { status = "error", message = "ไม่พบอีเมลของพนักงานคนนี้" });

                string token = _adminRepo.GeneratePasswordResetToken(username);
                string resetLink = Url.Action("SetupPassword", "Auth", new { token = token }, Request.Url.Scheme);

                string body = $@"
<div style='font-family: Arial, sans-serif; color: #333; line-height: 1.6; max-width: 600px; margin: 0 auto; border: 1px solid #ddd; padding: 20px; border-radius: 8px;'>
    <h2 style='color: #00b09b;'>Tablet Check-IN Center</h2>
    <p>เรียนคุณ <b>{username}</b>,</p>
    <p>คุณได้รับการเชิญให้เข้าใช้งานระบบ หรือมีการร้องขอเพื่อตั้งค่ารหัสผ่านใหม่</p>
    <p>กรุณาคลิกที่ปุ่มด้านล่าง (ลิงก์มีอายุ 24 ชั่วโมง):</p>
    <div style='text-align: center; margin: 30px 0;'>
        <a href='{resetLink}' style='background-color: #00b09b; color: #ffffff; padding: 12px 25px; text-decoration: none; border-radius: 5px; font-weight: bold; font-size: 16px;'>คลิกเพื่อตั้งค่ารหัสผ่าน</a>
    </div>
    <p style='font-size: 12px; color: #999;'>หากคุณไม่ได้เป็นผู้ร้องขอ กรุณาเพิกเฉยต่ออีเมลฉบับนี้</p>
    <hr style='border: 0; border-top: 1px solid #eee; margin: 20px 0;' />
    <p style='font-size: 11px; color: #aaa; text-align: center;'>ระบบแจ้งเตือนอัตโนมัติ ห้ามตอบกลับ</p>
</div>";

                EmailService.Send("[Action Required] ตั้งค่ารหัสผ่าน Tablet Check-IN", email, body);

                return Json(new { status = "success" });
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[Email Error] : {ex.Message}");
                return Json(new { status = "error", message = "เกิดข้อผิดพลาดในการส่งอีเมล: " + ex.Message });
            }
        }

        // ================= DEVICES API (For Admin View) =================

        [HttpGet]
        public JsonResult GetDevices()
        {
            try
            {
                var list = _deviceRepo.GetList(Session["Username"].ToString(), "All");
                return Json(list, JsonRequestBehavior.AllowGet);
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[System Error] : {ex.Message}");
                return Json(new { status = "error", message = "ระบบขัดข้องชั่วคราว ไม่สามารถดึงข้อมูลได้ กรุณาติดต่อแผนก IT" });
            }
        }

        [HttpGet]
        public JsonResult GetDeviceLog(string asset_no, int offset = 0)
        {
            var logs = _monitorRepo.GetHistoryLogs(asset_no, offset);
            return Json(logs, JsonRequestBehavior.AllowGet);
        }

        [HttpGet]
        public JsonResult GetDeviceChangeLog(int id, int offset = 0)
        {
            var logs = _deviceRepo.GetDeviceLogs(id, offset);
            return Json(logs, JsonRequestBehavior.AllowGet);
        }
    }
}
