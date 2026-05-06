using System;
using System.Web.Mvc;
using TabletCheckIn.Repositories;
using TabletCheckIn.Models;
using TabletCheckIn.Utility;

namespace TabletCheckIn.Controllers
{
    public class AdminController : Controller
    {
        private readonly AdminRepository _adminRepo = new AdminRepository();
        // ยืมใช้ Repo ของ Device และ Monitor มาแสดงในตารางหน้า Admin
        private readonly DeviceRepository _deviceRepo = new DeviceRepository();
        private readonly MonitorRepository _monitorRepo = new MonitorRepository();

        // 1. ตรวจสอบสิทธิ์และแสดงหน้า HTML
        [AppAuthorize(AllowedRoles = "Admin")]
        [HttpGet]
        public ActionResult Index()
        {
            if (Session["Username"] == null || Session["UserRole"]?.ToString() != "Admin")
            {
                return RedirectToAction("Index", "Device"); // ถ้าไม่ใช่ Admin เด้งกลับไปหน้าจัดการปกติ
            }

            ViewBag.Title = "Admin Management";
            return View();
        }

        // ================= USERS API =================
        [HttpGet]
        public JsonResult GetUsers()
        {
            if (Session["UserRole"]?.ToString() != "Admin") return Json(new { status = "error" }, JsonRequestBehavior.AllowGet);
            return Json(_adminRepo.GetUsers(), JsonRequestBehavior.AllowGet);
        }

        [HttpPost]
        public JsonResult SaveUser(UserSaveRequest req)
        {
            if (Session["UserRole"]?.ToString() != "Admin") return Json(new { status = "error", message = "Access Denied." });
            if (string.IsNullOrWhiteSpace(req.username))
                return Json(new { status = "error", message = "Username is required." });

            // บังคับว่าถ้ากรอกอีเมลมา ต้องมี @mail.canon
            if (!string.IsNullOrWhiteSpace(req.email) && !req.email.Trim().ToLower().EndsWith("@mail.canon"))
            {
                return Json(new { status = "error", message = "Email must end with @mail.canon" });
            }

            try
            {
                _adminRepo.SaveUser(req);

                // หลังจาก Save เสร็จ ให้ส่งค่าบอกหน้าบ้านว่าคนนี้มี Email ให้ถามต่อไหมว่าอยากส่งลิงก์หรือเปล่า
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
            if (Session["UserRole"]?.ToString() != "Admin") return Json(new { status = "error" });
            try
            {
                _adminRepo.DeleteUser(id);
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

        // ================= DEPTS API =================
        [HttpGet]
        public JsonResult GetDepartments()
        {
            if (Session["UserRole"]?.ToString() != "Admin") return Json(new { status = "error" }, JsonRequestBehavior.AllowGet);
            return Json(_adminRepo.GetDepartments(), JsonRequestBehavior.AllowGet);
        }

        [HttpPost]
        public JsonResult SaveDepartment(DeptSaveRequest req)
        {
            if (Session["UserRole"]?.ToString() != "Admin") return Json(new { status = "error", message = "Access Denied." });
            if (string.IsNullOrWhiteSpace(req.dept_name)) return Json(new { status = "error", message = "Department name is required." });

            try
            {
                _adminRepo.SaveDepartment(req);
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
        public JsonResult DeleteDepartment(int id)
        {
            if (Session["UserRole"]?.ToString() != "Admin") return Json(new { status = "error" });
            try
            {
                _adminRepo.DeleteDepartment(id);
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

        // ================= SEND EMAIL SETUP LINK =================
        [HttpPost]
        public JsonResult SendSetupLink(string username)
        {
            if (Session["UserRole"]?.ToString() != "Admin") return Json(new { status = "error", message = "Access Denied." });

            try
            {
                // 1. ดึง Email
                string email = _adminRepo.GetUserEmail(username);
                if (string.IsNullOrEmpty(email))
                    return Json(new { status = "error", message = "ไม่พบอีเมลของพนักงานคนนี้" });

                // 2. สร้าง Token
                string token = _adminRepo.GeneratePasswordResetToken(username);

                // 3. สร้าง URL สำหรับรีเซ็ตรหัสผ่าน (ชี้ไปที่ AuthController)
                string resetLink = Url.Action("SetupPassword", "Auth", new { token = token }, Request.Url.Scheme);

                // 4. สร้างเนื้อหาอีเมล (HTML)
                string body = $@"
                <div style='font-family: Arial, sans-serif; color: #333; line-height: 1.6; max-width: 600px; margin: 0 auto; border: 1px solid #ddd; padding: 20px; border-radius: 8px;'>
                    <h2 style='color: #00b09b;'>Tablet Check-IN Center</h2>
                    <p>เรียนคุณ <b>{username}</b>,</p>
                    <p>คุณได้รับการเชิญให้เข้าใช้งานระบบ หรือมีการร้องขอเพื่อตั้งค่ารหัสผ่านใหม่</p>
                    <p>กรุณาคลิกที่ปุ่มด้านล่างเพื่อตั้งค่ารหัสผ่านของคุณ (ลิงก์นี้มีอายุการใช้งาน 24 ชั่วโมง):</p>
                    <div style='text-align: center; margin: 30px 0;'>
                        <a href='{resetLink}' style='background-color: #00b09b; color: #ffffff; padding: 12px 25px; text-decoration: none; border-radius: 5px; font-weight: bold; font-size: 16px;'>คลิกเพื่อตั้งค่ารหัสผ่าน</a>
                    </div>
                    <p style='font-size: 12px; color: #999;'>หากคุณไม่ได้เป็นผู้ร้องขอ กรุณาเพิกเฉยต่ออีเมลฉบับนี้</p>
                    <hr style='border: 0; border-top: 1px solid #eee; margin: 20px 0;' />
                    <p style='font-size: 11px; color: #aaa; text-align: center;'>ระบบแจ้งเตือนอัตโนมัติ ห้ามตอบกลับ</p>
                </div>";

                // 5. สั่งยิงอีเมล
                SendEmailHelper("[Action Required] ตั้งค่ารหัสผ่าน Tablet Check-IN", email, body);

                return Json(new { status = "success" });
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[Email Error] : {ex.Message}");
                return Json(new { status = "error", message = "เกิดข้อผิดพลาดในการส่งอีเมล: " + ex.Message });
            }
        }

        private void SendEmailHelper(string subject, string to, string body)
        {
            using (var mail = new System.Net.Mail.MailMessage())
            {
                mail.From = new System.Net.Mail.MailAddress("Tablet-Check-In@mail.canon");
                mail.To.Add(to);
                mail.Subject = subject;
                mail.Body = body;
                mail.IsBodyHtml = true; // เปิดใช้งาน HTML

                using (var smtp = new System.Net.Mail.SmtpClient("nonauth-smtp.global.canon.co.jp", 25))
                {
                    smtp.DeliveryMethod = System.Net.Mail.SmtpDeliveryMethod.Network;
                    smtp.UseDefaultCredentials = false;
                    smtp.Send(mail);
                }
            }
        }

        // ================= DEVICES API (For Admin View) =================
        [HttpGet]
        public JsonResult GetDevices()
        {
            if (Session["UserRole"]?.ToString() != "Admin") return Json(new { status = "error" }, JsonRequestBehavior.AllowGet);
            try
            {
                // โหลดทุกอุปกรณ์ (ส่ง All ไปยัง Repo)
                var list = _deviceRepo.GetList(Session["Username"].ToString(), "All");
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
        public JsonResult GetDeviceLog(string asset_no, int offset = 0)
        {
            var logs = _monitorRepo.GetHistoryLogs(asset_no, offset); // ดึงประวัติ Check-in
            return Json(logs, JsonRequestBehavior.AllowGet);
        }

        [HttpGet]
        public JsonResult GetDeviceChangeLog(int id, int offset = 0)
        {
            var logs = _deviceRepo.GetDeviceLogs(id, offset); // ดึงประวัติการแก้ไขอุปกรณ์
            return Json(logs, JsonRequestBehavior.AllowGet);
        }
    }
}