using System;
using System.Web.Mvc;
using TabletCheckIn.Models;
using TabletCheckIn.Repositories;

namespace TabletCheckIn.Controllers
{
    public class AuthController : Controller
    {
        private readonly AuthRepository _repo = new AuthRepository();

        // 1. ส่งหน้า HTML Login (เทียบเท่า Page_Load โค้ดเดิม)
        [HttpGet]
        public ActionResult Index()
        {
            // ถ้ามี Session อยู่แล้ว ให้เด้งไปหน้า Monitor (หรือ Admin ก็ได้)
            if (Session["Username"] != null)
            {
                return RedirectToAction("Index", "Monitor");
            }

            ViewBag.Title = "Login";
            return View();
        }

        // 2. รับค่าตอนกดปุ่ม Login (รับข้อมูลมาเป็น JSON ผ่านคลาส LoginRequest)
        [HttpPost]
        [ValidateAntiForgeryToken]
        public JsonResult DoLogin(LoginRequest req)
        {
            // ดักจับกรณีผู้ใช้ไม่กรอกข้อมูล
            if (req == null || string.IsNullOrWhiteSpace(req.Username) || string.IsNullOrWhiteSpace(req.Password))
            {
                return Json(new { status = "error", message = "Please enter username and password." });
            }

            try
            {
                // ส่งไปเช็คใน DB
                var user = _repo.Authenticate(req.Username, req.Password);

                if (user != null)
                {
                    Session["Username"] = user.Username;
                    Session["UserRole"] = user.UserRole;
                    Session["UserDept"] = user.Department;
                    Session["FullName"] = user.FullName;

                    // ส่ง URL กลับไปให้ Javascript จัดการ Redirect
                    // (ส่งไปหน้า Monitor เป็นค่าเริ่มต้น หรือถ้าเป็น Admin จะให้เด้งไปหน้า Admin ก็เช็คเงื่อนไขตรงนี้ได้ครับ)
                    return Json(new { status = "success", redirectUrl = Url.Action("Index", "Monitor") });
                }
                else
                {
                    return Json(new { status = "error", message = "Invalid username or password." });
                }
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

        // 3. ฟังก์ชันสำหรับ Logout (เคลียร์ Session)
        [HttpPost]
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

            // เรียกใช้ Repo แทนการเขียน SQL ตรงๆ
            string user = _repo.GetUsernameByValidToken(token);

            if (string.IsNullOrEmpty(user))
            {
                ViewBag.ErrorMsg = "ลิงก์นี้หมดอายุหรือถูกใช้งานไปแล้ว กรุณาติดต่อ Admin";
                return View("TokenError"); // โชว์หน้า Error
            }

            ViewBag.Token = token;
            ViewBag.Username = user;
            return View(); // เปิดหน้า SetupPassword.cshtml
        }

        [AllowAnonymous]
        [HttpPost]
        public JsonResult DoSetupPassword(string token, string new_password)
        {
            if (string.IsNullOrEmpty(token) || string.IsNullOrEmpty(new_password))
                return Json(new { status = "error", message = "ข้อมูลไม่ครบถ้วน" });

            try
            {
                // โยนหน้าที่การอัปเดตให้ Repo จัดการ
                _repo.ResetPasswordWithToken(token, new_password);

                return Json(new { status = "success", redirectUrl = Url.Action("Index", "Auth") });
            }
            catch (Exception ex)
            {
                // ถ้าเกิด Exception (เช่น Token หมดอายุ) จาก Repo ให้พ่นข้อความ Error กลับไป
                return Json(new { status = "error", message = ex.Message });
            }
        }

        // ================= FORGOT PASSWORD (แบบที่ 2: ส่งรหัสผ่านเข้า Email ตรงๆ) =================
        [HttpPost]
        [AllowAnonymous]
        public JsonResult SendPasswordDirectly(string username)
        {
            if (string.IsNullOrWhiteSpace(username))
                return Json(new { status = "error", message = "กรุณากรอก Username หรือรหัสพนักงาน" });

            try
            {
                var userCreds = _repo.GetUserCredentials(username);
                if (userCreds == null || string.IsNullOrEmpty(userCreds.email))
                {
                    return Json(new { status = "error", message = "ไม่พบอีเมลที่ผูกกับรหัสพนักงานนี้ในระบบ กรุณาติดต่อ Admin" });
                }

                string email = userCreds.email;
                string password = userCreds.password;
                string fullName = string.IsNullOrEmpty(userCreds.full_name) ? username : userCreds.full_name;

                // 🌟 ปรับ Body ให้เป็น Plain Text ธรรมดา (ใช้ $@"..." เพื่อให้ขึ้นบรรทัดใหม่ได้ตามที่พิมพ์)
                string body = $@"เรียนคุณ {fullName},

                    ระบบได้ทำการส่งรหัสผ่านของคุณตามที่มีการร้องขอ

                    รหัสผ่านของคุณคือ: {password}

                    * คำแนะนำ: เพื่อความปลอดภัย กรุณาลบอีเมลฉบับนี้ทิ้งหลังจากเข้าสู่ระบบสำเร็จ

                    --------------------------------------------------
                    ระบบแจ้งเตือนอัตโนมัติ ห้ามตอบกลับ
                    Tablet Check-IN Center";

                // 🌟 เพิ่มพารามิเตอร์ false ต่อท้าย เพื่อบอกว่าไม่ใช้ HTML
                SendEmailHelper("[Action Required] แจ้งรหัสผ่านเข้าใช้งาน Tablet Check-IN", email, body, false);

                return Json(new { status = "success" });
            }
            catch (Exception ex)
            {
                return Json(new { status = "error", message = "เกิดข้อผิดพลาดในการส่งอีเมล: " + ex.Message });
            }
        }

        // 🌟 เพิ่มพารามิเตอร์ isHtml เข้ามาควบคุม
        private void SendEmailHelper(string subject, string to, string body, bool isHtml = true)
        {
            using (var mail = new System.Net.Mail.MailMessage())
            {
                mail.From = new System.Net.Mail.MailAddress("Tablet-Check-In@mail.canon");
                mail.To.Add(to);
                mail.Subject = subject;
                mail.Body = body;

                // 🌟 ใช้ค่าจากพารามิเตอร์มากำหนดรูปแบบ
                mail.IsBodyHtml = isHtml;

                using (var smtp = new System.Net.Mail.SmtpClient("nonauth-smtp.global.canon.co.jp", 25))
                {
                    smtp.DeliveryMethod = System.Net.Mail.SmtpDeliveryMethod.Network;
                    smtp.UseDefaultCredentials = false;
                    smtp.Send(mail);
                }
            }
        }

        // 📌 ฟังก์ชันลืมรหัสผ่านแบบเก่า (ส่งลิงก์แบบ Token) เก็บไว้ใช้ทีหลัง
        // ================= FORGOT PASSWORD API =================
        [HttpPost]
        [AllowAnonymous]
        public JsonResult ForgotPassword(string username)
        {
            if (string.IsNullOrWhiteSpace(username))
                return Json(new { status = "error", message = "กรุณากรอก Username หรือรหัสพนักงาน" });

            try
            {
                // 1. หา Email ของ User
                string email = _repo.GetUserEmail(username);
                if (string.IsNullOrEmpty(email))
                {
                    return Json(new { status = "error", message = "ไม่พบอีเมลที่ผูกกับรหัสพนักงานนี้ในระบบ กรุณาติดต่อ Admin" });
                }

                // 2. สร้าง Token สำหรับรีเซ็ตรหัส
                string token = _repo.GeneratePasswordResetToken(username);

                // 3. สร้าง URL ลิงก์ไปหน้า Setup Password
                string resetLink = Url.Action("SetupPassword", "Auth", new { token = token }, Request.Url.Scheme);

                // 4. สร้างเนื้อหาอีเมล (HTML)
                string body = $@"
                <div style='font-family: Arial, sans-serif; color: #333; line-height: 1.6; max-width: 600px; margin: 0 auto; border: 1px solid #ddd; padding: 20px; border-radius: 8px;'>
                    <h2 style='color: #00b09b;'>Tablet Check-IN Center</h2>
                    <p>เรียนคุณ <b>{username}</b>,</p>
                    <p>มีการร้องขอเพื่อตั้งค่ารหัสผ่านใหม่ (Reset Password)</p>
                    <p>กรุณาคลิกที่ปุ่มด้านล่างเพื่อตั้งค่ารหัสผ่านของคุณ (ลิงก์นี้มีอายุการใช้งาน 24 ชั่วโมง):</p>
                    <div style='text-align: center; margin: 30px 0;'>
                        <a href='{resetLink}' style='background-color: #00b09b; color: #ffffff; padding: 12px 25px; text-decoration: none; border-radius: 5px; font-weight: bold; font-size: 16px;'>ตั้งค่ารหัสผ่านใหม่</a>
                    </div>
                    <p style='font-size: 12px; color: #999;'>หากคุณไม่ได้เป็นผู้ร้องขอ กรุณาเพิกเฉยต่ออีเมลฉบับนี้</p>
                    <hr style='border: 0; border-top: 1px solid #eee; margin: 20px 0;' />
                    <p style='font-size: 11px; color: #aaa; text-align: center;'>ระบบแจ้งเตือนอัตโนมัติ ห้ามตอบกลับ</p>
                </div>";

                // 5. ส่ง Email
                SendEmailHelper("[Action Required] รีเซ็ตรหัสผ่าน Tablet Check-IN", email, body);

                return Json(new { status = "success" });
            }
            catch (Exception ex)
            {
                return Json(new { status = "error", message = "เกิดข้อผิดพลาดในการส่งอีเมล: " + ex.Message });
            }
        }

        // ฟังก์ชันช่วยส่งอีเมล (ก๊อปมาไว้ที่นี่ด้วย จะได้ใช้งานได้เลย)
        private void SendEmailHelper(string subject, string to, string body)
        {
            using (var mail = new System.Net.Mail.MailMessage())
            {
                mail.From = new System.Net.Mail.MailAddress("Tablet-Check-In@mail.canon");
                mail.To.Add(to);
                mail.Subject = subject;
                mail.Body = body;
                mail.IsBodyHtml = true;

                using (var smtp = new System.Net.Mail.SmtpClient("nonauth-smtp.global.canon.co.jp", 25))
                {
                    smtp.DeliveryMethod = System.Net.Mail.SmtpDeliveryMethod.Network;
                    smtp.UseDefaultCredentials = false;
                    smtp.Send(mail);
                }
            }
        }

    }
}