using System;
using System.Collections.Generic;
using System.Linq;
using Npgsql;
using Dapper; 
using TabletCheckIn.Models;
using Check_Sheet_Online;

namespace TabletCheckIn.Repositories
{
    public class DeviceRepository
    {
        public List<DeviceListModel> GetList(string currentUser, string userDeptSession)
        {
            using (var conn = PostgreSqlDbConnection.GetConnection())
            {
                string sql = @"
                    SELECT 
                        d.id, d.asset_no, d.status, d.host_name, d.owner_name, 
                        d.dept_name, d.detail, d.check_morn, d.check_night, 
                        TO_CHAR(d.registered_at, 'DD/MM/YYYY HH24:MI') as registered_at, 
                        COALESCE(u.full_name, d.last_updated_by, '-') as last_updated_by
                    FROM tablet_check_in.devices d
                    LEFT JOIN tablet_check_in.users u ON u.username = d.last_updated_by ";

                // สร้าง Dynamic Parameters สำหรับส่งค่า (ถ้ามี)
                var parameters = new DynamicParameters();

                if (userDeptSession != "All" && userDeptSession != "Admin")
                {
                    sql += @" WHERE d.dept_name IN (
                                SELECT dept_name FROM tablet_check_in.user_departments 
                                WHERE username = @currUser
                            ) ";
                    parameters.Add("currUser", currentUser);
                }

                sql += " ORDER BY d.id DESC";

                // ยิง Dapper ทีเดียวจบ! 
                return conn.Query<DeviceListModel>(sql, parameters).ToList();
            }
        }

        public void SaveDevice(DeviceSaveRequest req, string currentUser, string currentName)
        {
            bool isUpdate = !string.IsNullOrWhiteSpace(req.data_id);
            int deviceId = 0;
            string userLogInfo = string.IsNullOrEmpty(currentName) ? currentUser : $"{currentUser} ({currentName})";

            using (var conn = PostgreSqlDbConnection.GetConnection())
            {
                conn.Open();
                using (var tx = conn.BeginTransaction())
                {
                    if (isUpdate)
                    {
                        deviceId = int.Parse(req.data_id);

                        // ดึงข้อมูลเก่ามาเทียบด้วย Dapper
                        var oldData = conn.QuerySingleOrDefault<dynamic>(
                            "SELECT status, check_morn, check_night FROM tablet_check_in.devices WHERE id = @id",
                            new { id = deviceId }, tx);

                        string oldStatus = oldData?.status ?? "";
                        bool oldMorn = oldData?.check_morn ?? false;
                        bool oldNight = oldData?.check_night ?? false;

                        bool configChanged = (oldMorn != req.check_morn) ||
                                             (oldNight != req.check_night) ||
                                             (!string.Equals(oldStatus, req.status, StringComparison.OrdinalIgnoreCase));

                        // อัปเดตข้อมูลด้วย Dapper
                        string sqlUpdate = @"
                            UPDATE tablet_check_in.devices
                            SET asset_no=@a, status=@s, host_name=@h,
                                owner_name=@o, dept_name=@d, detail=@det, check_morn=@cm, check_night=@cn,
                                last_updated_by=@user
                            WHERE id=@id";

                        conn.Execute(sqlUpdate, new
                        {
                            a = req.asset_no,
                            s = req.status,
                            h = req.host_name ?? "",
                            o = req.owner ?? "",
                            d = req.dept ?? "",
                            det = req.detail ?? "",
                            cm = req.check_morn,
                            cn = req.check_night,
                            user = currentUser,
                            id = deviceId
                        }, tx);

                        // จัดการ History Config (เรียกเมธอดที่แก้เป็น Dapper แล้วด้านล่าง)
                        EnsureConfigHistory(conn, tx, req.asset_no, req.status, req.check_morn, req.check_night, true, configChanged);

                        string baseLog = string.IsNullOrEmpty(req.change_details) ? $"Updated info for {req.asset_no}" : req.change_details;
                        string fullLog = $"[User: {userLogInfo}] {baseLog}";

                        InsertHistoryLog(conn, tx, deviceId, "Update", fullLog);
                    }
                    else
                    {
                        // Insert และเอา ID ล่าสุดกลับมาด้วย Dapper (RETURNING id)
                        string sqlInsert = @"
                            INSERT INTO tablet_check_in.devices 
                                (asset_no, status, host_name, owner_name, dept_name, detail, check_morn, check_night, registered_at, last_updated_by)
                            VALUES 
                                (@a, @s, @h, @o, @d, @det, @cm, @cn, NOW(), @user) 
                            RETURNING id";

                        deviceId = conn.QuerySingle<int>(sqlInsert, new
                        {
                            a = req.asset_no,
                            s = req.status,
                            h = req.host_name ?? "",
                            o = req.owner ?? "",
                            d = req.dept ?? "",
                            det = req.detail ?? "",
                            cm = req.check_morn,
                            cn = req.check_night,
                            user = currentUser
                        }, tx);

                        string fullLogReg = $"[User: {userLogInfo}] Registered: {req.asset_no} ({req.status})";
                        InsertHistoryLog(conn, tx, deviceId, "Register", fullLogReg);
                        InsertConfigHistory(conn, tx, req.asset_no, req.status, req.check_morn, req.check_night, DateTime.Now.Date);
                    }

                    tx.Commit();
                }
            }
        }

        public void DeleteDevice(int id)
        {
            using (var conn = PostgreSqlDbConnection.GetConnection())
            {
                conn.Open();
                using (var tx = conn.BeginTransaction())
                {
                    // ดึงชื่อ Asset ด้วย Dapper
                    string assetNo = conn.QuerySingleOrDefault<string>("SELECT asset_no FROM tablet_check_in.devices WHERE id=@id", new { id = id }, tx);

                    // ลบ Device 
                    conn.Execute("DELETE FROM tablet_check_in.devices WHERE id=@id", new { id = id }, tx);

                    // ลบ Config History ทิ้งด้วย
                    if (!string.IsNullOrEmpty(assetNo))
                    {
                        conn.Execute("DELETE FROM tablet_check_in.device_config_history WHERE asset_no=@a", new { a = assetNo }, tx);
                    }

                    tx.Commit();
                }
            }
        }

        public List<DeviceLogModel> GetDeviceLogs(int deviceId, int offset)
        {
            using (var conn = PostgreSqlDbConnection.GetConnection())
            {
                string sql = @"
                    SELECT 
                        action_type as action, 
                        change_details as details, 
                        TO_CHAR(created_at, 'DD/MM/YYYY HH24:MI') as date
                    FROM tablet_check_in.device_history 
                    WHERE device_id=@did 
                    ORDER BY created_at DESC 
                    LIMIT 20 OFFSET @offset";

                return conn.Query<DeviceLogModel>(sql, new { did = deviceId, offset = offset }).ToList();
            }
        }

        // ================= HELPER METHODS (Dapper Version) =================
        private void EnsureConfigHistory(NpgsqlConnection conn, NpgsqlTransaction tx, string assetNo, string status, bool morn, bool night, bool forceInitIfNone, bool writeNewIfChanged)
        {
            // เช็คว่ามีค่าในระบบหรือยัง
            bool hasAny = conn.ExecuteScalar<int?>("SELECT 1 FROM tablet_check_in.device_config_history WHERE asset_no = @a LIMIT 1", new { a = assetNo }, tx) != null;

            if (!hasAny && forceInitIfNone)
            {
                InsertConfigHistory(conn, tx, assetNo, status, morn, night, DateTime.Now.Date);
            }

            if (!writeNewIfChanged) return;

            // หา ID ของวันนี้
            int todayId = conn.ExecuteScalar<int>(@"
                SELECT id FROM tablet_check_in.device_config_history 
                WHERE asset_no = @a AND effective_date = CURRENT_DATE 
                ORDER BY updated_at DESC LIMIT 1", new { a = assetNo }, tx);

            if (todayId > 0)
            {
                // อัปเดตของเดิม
                conn.Execute(@"
                    UPDATE tablet_check_in.device_config_history 
                    SET check_morn = @m, check_night = @n, status = @s, updated_at = CURRENT_TIMESTAMP 
                    WHERE id = @id", new { m = morn, n = night, s = status, id = todayId }, tx);
            }
            else
            {
                // สร้างใหม่ของวันนี้
                InsertConfigHistory(conn, tx, assetNo, status, morn, night, DateTime.Now.Date);
            }
        }

        private void InsertConfigHistory(NpgsqlConnection conn, NpgsqlTransaction tx, string assetNo, string status, bool morn, bool night, DateTime effectiveDate)
        {
            string sql = @"
                INSERT INTO tablet_check_in.device_config_history (asset_no, status, check_morn, check_night, effective_date) 
                VALUES (@a, @s, @m, @n, @eff)";

            conn.Execute(sql, new { a = assetNo, s = status, m = morn, n = night, eff = effectiveDate.Date }, tx);
        }

        private void InsertHistoryLog(NpgsqlConnection conn, NpgsqlTransaction tx, int deviceId, string action, string details)
        {
            string sql = @"
                INSERT INTO tablet_check_in.device_history (device_id, action_type, change_details) 
                VALUES (@did, @act, @dt)";

            conn.Execute(sql, new { did = deviceId, act = action, dt = details }, tx);
        }
    }
}