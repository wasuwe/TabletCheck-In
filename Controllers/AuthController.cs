using System;
using System.Text.RegularExpressions;
using System.Web.Mvc;
using TabletCheckIn.Models;
using TabletCheckIn.Repositories;
using TabletCheckIn.Utility;

namespace TabletCheckIn.Controllers
{
    public class AuthController : Controller
    {
        private readonly AuthRepository _repo = new AuthRepository();

        [HttpGet]
        public ActionResult Index()
        {
            if (Session["Username"] != null)
                return RedirectToAction("Index", "Monitor");

            ViewBag.Title = "Login";
            return View();
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public JsonResult DoLogin(LoginRequest req)
        {
            if (req == null || string.IsNullOrWhiteSpace(req.Username) || string.IsNullOrWhiteSpace(req.Password))
                return Json(new { status = "error", message = "Please enter username and password." });

            try
            {
                var user = _repo.Authenticate(req.Username, req.Password);

                if (user != null)
                {
                    Session["Username"] = user.Username;
                    Session["UserRole"] = user.UserRole;
                    Session["UserDept"] = user.Department;
                    Session["FullName"] = user.FullName;
                    return Json(new { status = "success", redirectUrl = Url.Action("Index", "Monitor") });
                }

                return Json(new { status = "error", message = "Invalid username or password." });
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[System Error] : {ex.Message}");
                return Json(new { status = "error", message = "ระบบขัดข้องชั่วคราว ไม่สามารถดึงข้อมูลได้ กรุณาติดต่อแผนก IT" });
            }
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public JsonResult Logout()
        {
            Session.Clear();
            Session.Abandon();
            return Json(new { status = "success", redirectUrl = Url.Action("Index", "Auth") });
        }

        // ================= SETUP / RESET PASSWORD =================

        [AllowAnonymous]
        [HttpGet]
        public ActionResult SetupPassword(string token)
        {
            if (string.IsNullOrEmpty(token)) return RedirectToAction("Index");

            string user = _repo.GetUsernameByValidToken(token);

            if (string.IsNullOrEmpty(user))
            {
                ViewBag.ErrorMsg = "ลิงก์นี้หมดอายุหรือถูกใช้งานไปแล้ว กรุณาติดต่อ Admin";
                return View("TokenError");
            }

            ViewBag.Token = token;
            ViewBag.Username = user;
            return View();
        }

        [AllowAnonymous]
        [HttpPost]
        public JsonResult DoSetupPassword(string token, string new_password)
        {
            if (string.IsNullOrEmpty(token) || string.IsNullOrEmpty(new_password))
                return Json(new { status = "error", message = "ข้อมูลไม่ครบถ้วน" });

            if (!IsPasswordStrong(new_password))
                return Json(new { status = "error", message = "รหัสผ่านต้องมีความยาวอย่างน้อย 8 ตัวอักษร และมีตัวเลขอย่างน้อย 1 ตัว" });

            try
            {
                _repo.ResetPasswordWithToken(token, new_password);
                return Json(new { status = "success", redirectUrl = Url.Action("Index", "Auth") });
            }
            catch (Exception ex)
            {
                return Json(new { status = "error", message = ex.Message });
            }
        }

        // ================= FORGOT PASSWORD (ส่ง Reset Link) =================

        [HttpPost]
        [AllowAnonymous]
        public JsonResult SendPasswordDirectly(string username)
        {
            if (string.IsNullOrWhiteSpace(username))
                return Json(new { status = "error", message = "กรุณากรอก Username หรือรหัสพนักงาน" });

            try
            {
                string email = _repo.GetUserEmail(username);
                if (string.IsNullOrEmpty(email))
                    return Json(new { status = "error", message = "ไม่พบอีเมลที่ผูกกับรหัสพนักงานนี้ในระบบ กรุณาติดต่อ Admin" });

                string token = _repo.GeneratePasswordResetToken(username);
                string resetLink = Url.Action("SetupPassword", "Auth", new { token = token }, Request.Url.Scheme);

                string body = BuildResetEmailBody(username, resetLink);
                EmailService.Send("[Action Required] ตั้งค่ารหัสผ่าน Tablet Check-IN", email, body);

                return Json(new { status = "success" });
            }
            catch (Exception ex)
            {
                return Json(new { status = "error", message = "เกิดข้อผิดพลาดในการส่งอีเมล: " + ex.Message });
            }
        }

        [HttpPost]
        [AllowAnonymous]
        public JsonResult ForgotPassword(string username)
        {
            if (string.IsNullOrWhiteSpace(username))
                return Json(new { status = "error", message = "กรุณากรอก Username หรือรหัสพนักงาน" });

            try
            {
                string email = _repo.GetUserEmail(username);
                if (string.IsNullOrEmpty(email))
                    return Json(new { status = "error", message = "ไม่พบอีเมลที่ผูกกับรหัสพนักงานนี้ในระบบ กรุณาติดต่อ Admin" });

                string token = _repo.GeneratePasswordResetToken(username);
                string resetLink = Url.Action("SetupPassword", "Auth", new { token = token }, Request.Url.Scheme);

                string body = BuildResetEmailBody(username, resetLink);
                EmailService.Send("[Action Required] รีเซ็ตรหัสผ่าน Tablet Check-IN", email, body);

                return Json(new { status = "success" });
            }
            catch (Exception ex)
            {
                return Json(new { status = "error", message = "เกิดข้อผิดพลาดในการส่งอีเมล: " + ex.Message });
            }
        }

        private static bool IsPasswordStrong(string password)
        {
            return password.Length >= 8 && Regex.IsMatch(password, @"\d");
        }

        private static string BuildResetEmailBody(string username, string resetLink)
        {
            return $@"
<div style='font-family: Arial, sans-serif; color: #333; line-height: 1.6; max-width: 600px; margin: 0 auto; border: 1px solid #ddd; padding: 20px; border-radius: 8px;'>
    <h2 style='color: #00b09b;'>Tablet Check-IN Center</h2>
    <p>เรียนคุณ <b>{username}</b>,</p>
    <p>มีการร้องขอเพื่อตั้งค่ารหัสผ่านใหม่ กรุณาคลิกที่ปุ่มด้านล่าง (ลิงก์มีอายุ 24 ชั่วโมง):</p>
    <div style='text-align: center; margin: 30px 0;'>
        <a href='{resetLink}' style='background-color: #00b09b; color: #ffffff; padding: 12px 25px; text-decoration: none; border-radius: 5px; font-weight: bold; font-size: 16px;'>ตั้งค่ารหัสผ่านใหม่</a>
    </div>
    <p style='font-size: 12px; color: #999;'>หากคุณไม่ได้เป็นผู้ร้องขอ กรุณาเพิกเฉยต่ออีเมลฉบับนี้</p>
    <hr style='border: 0; border-top: 1px solid #eee; margin: 20px 0;' />
    <p style='font-size: 11px; color: #aaa; text-align: center;'>ระบบแจ้งเตือนอัตโนมัติ ห้ามตอบกลับ</p>
</div>";
        }
    }
}
