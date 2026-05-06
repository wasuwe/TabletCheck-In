using System;
using System.Linq;
using Dapper; 
using TabletCheckIn.Models;
using Check_Sheet_Online;

namespace TabletCheckIn.Repositories
{
    public class AuthRepository
    {
        public UserProfile Authenticate(string username, string password)
        {
            try
            {
                using (var conn = PostgreSqlDbConnection.GetConnection())
                {
                    string sql = @"
                        SELECT 
                            u.username,
                            u.full_name as FullName, 
                            u.user_role as UserRole, 
                            STRING_AGG(ud.dept_name, ',') AS Department 
                        FROM tablet_check_in.users u
                        LEFT JOIN tablet_check_in.user_departments ud ON u.username = ud.username
                        WHERE u.username = @u AND u.password = @p
                        GROUP BY u.username, u.full_name, u.user_role";

                    // Dapper จะดึงข้อมูลแล้วจับคู่ใส่ UserProfile ให้อัตโนมัติ
                    var user = conn.QuerySingleOrDefault<UserProfile>(sql, new { u = username, p = password });

                    if (user != null)
                    {
                        // จัดการกรณีไม่มี Role ให้เป็น Normal
                        if (string.IsNullOrWhiteSpace(user.UserRole))
                        {
                            user.UserRole = "Normal";
                        }
                    }

                    return user;
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[Auth Error]: {ex.Message}");
                throw new Exception("Database connection error.");
            }
        }

        public string GetUsernameByValidToken(string token)
        {
            using (var conn = PostgreSqlDbConnection.GetConnection())
            {
                string sql = @"
                    SELECT username FROM tablet_check_in.password_reset_tokens 
                    WHERE token = @t AND is_used = FALSE AND expires_at > NOW()";

                return conn.QuerySingleOrDefault<string>(sql, new { t = token });
            }
        }

        // ฟังก์ชันสำหรับรีเซ็ตรหัสผ่านและอัปเดตสถานะ Token
        public void ResetPasswordWithToken(string token, string newPassword)
        {
            using (var conn = PostgreSqlDbConnection.GetConnection())
            {
                conn.Open();
                using (var tx = conn.BeginTransaction())
                {
                    // 1. ตรวจสอบ Token อีกรอบเพื่อความชัวร์ (ล็อกใน Transaction)
                    string sqlCheck = @"SELECT username FROM tablet_check_in.password_reset_tokens WHERE token = @t AND is_used = FALSE AND expires_at > NOW()";
                    string user = conn.QuerySingleOrDefault<string>(sqlCheck, new { t = token }, tx);

                    if (string.IsNullOrEmpty(user))
                    {
                        // โยน Error ออกไปให้ Controller จับ
                        throw new Exception("Token หมดอายุหรือไม่ถูกต้อง");
                    }

                    // 2. อัปเดตรหัสผ่านใหม่
                    conn.Execute("UPDATE tablet_check_in.users SET password = @p WHERE username = @u", new { p = newPassword, u = user }, tx);

                    // 3. Mark Token ว่าถูกใช้งานแล้ว
                    conn.Execute("UPDATE tablet_check_in.password_reset_tokens SET is_used = TRUE WHERE token = @t", new { t = token }, tx);

                    tx.Commit();
                }
            }
        }

        // ================= FORGOT PASSWORD =================

        // ฟังก์ชันใหม่: ดึง Email, Password และ FullName มาพร้อมกันเลย
        public dynamic GetUserCredentials(string username)
        {
            using (var conn = PostgreSqlDbConnection.GetConnection())
            {
                string sql = "SELECT email, password, full_name FROM tablet_check_in.users WHERE username=@u";
                // Dapper จะ map ลง dynamic object ให้เอง สามารถเรียก .email, .password ได้เลย
                return conn.QuerySingleOrDefault(sql, new { u = username });
            }
        }

        public string GetUserEmail(string username)
        {
            using (var conn = PostgreSqlDbConnection.GetConnection())
            {
                return conn.QuerySingleOrDefault<string>("SELECT email FROM tablet_check_in.users WHERE username=@u", new { u = username });
            }
        }

        public string GeneratePasswordResetToken(string username)
        {
            using (var conn = PostgreSqlDbConnection.GetConnection())
            {
                conn.Open();
                using (var tx = conn.BeginTransaction())
                {
                    // ยกเลิก Token เก่า
                    conn.Execute("UPDATE tablet_check_in.password_reset_tokens SET is_used = TRUE WHERE username = @u", new { u = username }, tx);

                    // สร้าง Token ใหม่
                    string token = Guid.NewGuid().ToString("N");
                    DateTime expiry = DateTime.Now.AddHours(24);

                    string sql = "INSERT INTO tablet_check_in.password_reset_tokens (username, token, expires_at) VALUES (@u, @t, @e)";
                    conn.Execute(sql, new { u = username, t = token, e = expiry }, tx);

                    tx.Commit();
                    return token;
                }
            }
        }
    }
}