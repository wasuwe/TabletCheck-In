namespace TabletCheckIn.Models
{
    // คลาสสำหรับรับค่าจากฟอร์มหน้าเว็บ
    public class LoginRequest
    {
        public string Username { get; set; }
        public string Password { get; set; }
    }

    // คลาสสำหรับเก็บข้อมูลผู้ใช้ที่ดึงมาจาก Database
    public class UserProfile
    {
        public string Username { get; set; }
        public string FullName { get; set; }
        public string UserRole { get; set; }
        public string Department { get; set; }
    }
}