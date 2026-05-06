using System.Web;
using System.Web.Mvc;

namespace TabletCheckIn.Utility
{
    public class AppAuthorizeAttribute : AuthorizeAttribute
    {
        public string AllowedRoles { get; set; } // สำหรับระบุสิทธิ์ เช่น "Admin", "Admin,Normal"

        protected override bool AuthorizeCore(HttpContextBase httpContext)
        {
            // 1. ตรวจสอบว่าล็อกอินหรือยัง?
            if (httpContext.Session["Username"] == null) return false;

            // 2. ถ้าดึง Role มาแล้วเป็นค่าว่าง (null) ให้ถือว่าเป็น "Normal" อัตโนมัติ
            string userRole = httpContext.Session["UserRole"]?.ToString();
            if (string.IsNullOrWhiteSpace(userRole))
            {
                userRole = "Normal";
            }

            // 3. ถ้าไม่ได้ระบุ AllowedRoles ไว้ที่ Controller แปลว่าแค่ "ล็อกอิน" ก็เข้าได้เลย
            if (string.IsNullOrEmpty(AllowedRoles)) return true;

            // 4. ตรวจสอบว่า Role ของ User ตรงกับที่อนุญาตหรือไม่
            var roles = AllowedRoles.Split(',');
            foreach (var role in roles)
            {
                if (role.Trim().Equals(userRole, System.StringComparison.OrdinalIgnoreCase))
                    return true;
            }

            return false; // ถ้าไม่ตรงเลย = ไม่มีสิทธิ์
        }

        protected override void HandleUnauthorizedRequest(AuthorizationContext filterContext)
        {
            // หากไม่มีสิทธิ์ ให้จัดการตามประเภทของ Request
            if (filterContext.HttpContext.Request.IsAjaxRequest())
            {
                filterContext.Result = new JsonResult
                {
                    Data = new { status = "error", message = "Access Denied. You don't have permission." },
                    JsonRequestBehavior = JsonRequestBehavior.AllowGet
                };
            }
            else
            {
                // ถ้าหน้าเว็บธรรมดา ให้เด้งไปหน้า Monitor และแสดงข้อความเตือน (หรือจะสร้างหน้า Error 403 แยกต่างหากก็ได้ครับ)
                filterContext.Result = new RedirectResult("~/Monitor/Index?error=access_denied");
            }
        }
    }
}