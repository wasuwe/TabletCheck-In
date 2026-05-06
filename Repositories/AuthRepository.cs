using System;
using Dapper;
using TabletCheckIn.Models;
using TabletCheckIn.Utility;
using Check_Sheet_Online;

namespace TabletCheckIn.Repositories
{
    public class AuthRepository
    {
        private class AuthUserRecord
        {
            public string Username { get; set; }
            public string StoredPassword { get; set; }
            public string FullName { get; set; }
            public string UserRole { get; set; }
            public string Department { get; set; }
        }

        public UserProfile Authenticate(string username, string password)
        {
            try
            {
                using (var conn = PostgreSqlDbConnection.GetConnection())
                {
                    string sql = @"
                        SELECT
                            u.username,
                            u.password AS StoredPassword,
                            u.full_name AS FullName,
                            u.user_role AS UserRole,
                            STRING_AGG(ud.dept_name, ',') AS Department
                        FROM tablet_check_in.users u
                        LEFT JOIN tablet_check_in.user_departments ud ON u.username = ud.username
                        WHERE u.username = @u
                        GROUP BY u.username, u.password, u.full_name, u.user_role";

                    var record = conn.QuerySingleOrDefault<AuthUserRecord>(sql, new { u = username });
                    if (record == null) return null;

                    if (!PasswordHasher.Verify(password, record.StoredPassword)) return null;

                    // Auto-upgrade plaintext passwords to hash on successful login
                    if (!PasswordHasher.IsHashed(record.StoredPassword))
                    {
                        string newHash = PasswordHasher.Hash(password);
                        conn.Execute("UPDATE tablet_check_in.users SET password = @p WHERE username = @u",
                            new { p = newHash, u = username });
                    }

                    return new UserProfile
                    {
                        Username = record.Username,
                        FullName = record.FullName,
                        UserRole = string.IsNullOrWhiteSpace(record.UserRole) ? "Normal" : record.UserRole,
                        Department = record.Department
                    };
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

        public void ResetPasswordWithToken(string token, string newPassword)
        {
            using (var conn = PostgreSqlDbConnection.GetConnection())
            {
                conn.Open();
                using (var tx = conn.BeginTransaction())
                {
                    string sqlCheck = @"
                        SELECT username FROM tablet_check_in.password_reset_tokens
                        WHERE token = @t AND is_used = FALSE AND expires_at > NOW()";
                    string user = conn.QuerySingleOrDefault<string>(sqlCheck, new { t = token }, tx);

                    if (string.IsNullOrEmpty(user))
                        throw new Exception("Token หมดอายุหรือไม่ถูกต้อง");

                    string hashedPassword = PasswordHasher.Hash(newPassword);
                    conn.Execute("UPDATE tablet_check_in.users SET password = @p WHERE username = @u",
                        new { p = hashedPassword, u = user }, tx);

                    conn.Execute("UPDATE tablet_check_in.password_reset_tokens SET is_used = TRUE WHERE token = @t",
                        new { t = token }, tx);

                    tx.Commit();
                }
            }
        }

        public string GetUserEmail(string username)
        {
            using (var conn = PostgreSqlDbConnection.GetConnection())
            {
                return conn.QuerySingleOrDefault<string>(
                    "SELECT email FROM tablet_check_in.users WHERE username=@u", new { u = username });
            }
        }

        public string GeneratePasswordResetToken(string username)
        {
            using (var conn = PostgreSqlDbConnection.GetConnection())
            {
                conn.Open();
                using (var tx = conn.BeginTransaction())
                {
                    conn.Execute(
                        "UPDATE tablet_check_in.password_reset_tokens SET is_used = TRUE WHERE username = @u",
                        new { u = username }, tx);

                    string token = Guid.NewGuid().ToString("N");
                    DateTime expiry = DateTime.Now.AddHours(24);

                    conn.Execute(
                        "INSERT INTO tablet_check_in.password_reset_tokens (username, token, expires_at) VALUES (@u, @t, @e)",
                        new { u = username, t = token, e = expiry }, tx);

                    tx.Commit();
                    return token;
                }
            }
        }
    }
}
