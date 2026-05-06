using System;
using System.Collections.Generic;
using System.Linq;
using Dapper;
using TabletCheckIn.Models;
using TabletCheckIn.Utility;
using Check_Sheet_Online;

namespace TabletCheckIn.Repositories
{
    public class AdminRepository
    {
        // ================= USERS =================
        public List<UserModel> GetUsers()
        {
            using (var conn = PostgreSqlDbConnection.GetConnection())
            {
                string sql = @"
            SELECT u.username, u.full_name, u.email, u.user_role, STRING_AGG(ud.dept_name, ',') AS dept_name
            FROM tablet_check_in.users u
            LEFT JOIN tablet_check_in.user_departments ud ON u.username = ud.username
            GROUP BY u.username, u.full_name, u.email, u.user_role
            ORDER BY u.username";

                return conn.Query<UserModel>(sql).ToList();
            }
        }

        public void SaveUser(UserSaveRequest req)
        {
            using (var conn = PostgreSqlDbConnection.GetConnection())
            {
                conn.Open();
                using (var tx = conn.BeginTransaction())
                {
                    string defaultPassword = PasswordHasher.Hash("Canon1234");

                    // ดักจับกรณีไม่ได้เลือก Role มา ให้เป็น User เบื้องต้น
                    string role = string.IsNullOrWhiteSpace(req.user_role) ? "User" : req.user_role;

                    // เพิ่ม user_role เข้าไปใน SQL ทั้งตอน INSERT และ UPDATE
                    string sqlUser = req.is_edit
                        ? "UPDATE tablet_check_in.users SET full_name=@f, email=@e, user_role=@r WHERE username=@u"
                        : "INSERT INTO tablet_check_in.users (username, password, full_name, email, user_role) VALUES (@u, @p, @f, @e, @r)";

                    conn.Execute(sqlUser, new
                    {
                        u = req.username,
                        p = defaultPassword,
                        f = req.full_name ?? "",
                        e = req.email ?? "",
                        r = role // ส่งตัวแปร Role เข้าไป
                    }, tx);

                    // ส่วนที่จัดการแผนก (user_departments) ปล่อยไว้เหมือนเดิม
                    conn.Execute("DELETE FROM tablet_check_in.user_departments WHERE username=@u", new { u = req.username }, tx);

                    if (!string.IsNullOrEmpty(req.dept_name))
                    {
                        string[] depts = req.dept_name.Split(new[] { ',' }, StringSplitOptions.RemoveEmptyEntries);
                        foreach (string d in depts)
                        {
                            string sqlMap = "INSERT INTO tablet_check_in.user_departments (username, dept_name) VALUES (@u, @d)";
                            conn.Execute(sqlMap, new { u = req.username, d = d.Trim() }, tx);
                        }
                    }
                    tx.Commit();
                }
            }
        }

        public void DeleteUser(string username)
        {
            using (var conn = PostgreSqlDbConnection.GetConnection())
            {
                conn.Execute("DELETE FROM tablet_check_in.users WHERE username=@u", new { u = username });
            }
        }

        // ================= TOKEN & EMAIL =================
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
                // 1. ยกเลิก Token เก่าทั้งหมดของ User คนนี้ (ป้องกันการสแปม)
                conn.Execute("UPDATE tablet_check_in.password_reset_tokens SET is_used = TRUE WHERE username = @u", new { u = username });

                // 2. สร้าง Token ใหม่แบบสุ่ม (32 ตัวอักษร)
                string token = Guid.NewGuid().ToString("N");
                DateTime expiry = DateTime.Now.AddHours(24); // ให้ลิงก์มีอายุ 24 ชั่วโมง

                // 3. บันทึกลงฐานข้อมูล
                string sql = "INSERT INTO tablet_check_in.password_reset_tokens (username, token, expires_at) VALUES (@u, @t, @e)";
                conn.Execute(sql, new { u = username, t = token, e = expiry });

                return token;
            }
        }

        // ================= DEPARTMENTS =================
        public List<DepartmentModel> GetDepartments()
        {
            using (var conn = PostgreSqlDbConnection.GetConnection())
            {
                string sql = "SELECT id, dept_name, email FROM tablet_check_in.departments ORDER BY id";
                return conn.Query<DepartmentModel>(sql).ToList();
            }
        }

        public void SaveDepartment(DeptSaveRequest req)
        {
            bool isEdit = !string.IsNullOrEmpty(req.id);
            using (var conn = PostgreSqlDbConnection.GetConnection())
            {
                string sql = isEdit
                    ? "UPDATE tablet_check_in.departments SET dept_name=@n, email=@e WHERE id=@id"
                    : "INSERT INTO tablet_check_in.departments (dept_name, email) VALUES (@n, @e)";

                int? parsedId = isEdit ? (int?)int.Parse(req.id) : null;

                conn.Execute(sql, new { n = req.dept_name, e = req.email ?? "", id = parsedId });
            }
        }

        public void DeleteDepartment(int id)
        {
            using (var conn = PostgreSqlDbConnection.GetConnection())
            {
                conn.Execute("DELETE FROM tablet_check_in.departments WHERE id=@id", new { id = id });
            }
        }
        
    }
}