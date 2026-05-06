using System.Web;

namespace TabletCheckIn.Utility
{
    public static class AuthHelper
    {
        // เช็คว่า User ปัจจุบันมีสิทธิ์ตรงกับที่ต้องการหรือไม่
        public static bool HasRole(string expectedRole)
        {
            if (HttpContext.Current.Session["Username"] == null) return false;

            string userRole = HttpContext.Current.Session["UserRole"]?.ToString();
            if (string.IsNullOrWhiteSpace(userRole))
            {
                userRole = "Normal"; // ถ้าไม่มีสิทธิ์ ให้เป็น Normal
            }

            return userRole.Equals(expectedRole, System.StringComparison.OrdinalIgnoreCase);
        }
    }
}