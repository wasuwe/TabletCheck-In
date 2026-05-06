using System;
using System.Collections.Generic;
using System.Configuration;
using System.Linq;
using System.Net.Mail;
using System.Text;
using System.Web.Http; // 🌟 เปลี่ยนมาใช้ Http สำหรับ Web API แท้ๆ
using Dapper;
using Check_Sheet_Online;
using System.Net;

namespace TabletCheckIn.Controllers.Api // แนะนำให้แยก Namespace เป็น Api
{
    [AllowAnonymous]
    [RoutePrefix("api/alert")] // 🌟 กำหนด URL แบบชัดเจนเลย
    public class AlertApiController : ApiController // 🌟 สืบทอดจาก ApiController
    {
        // URL: /api/alert/trigger?key=tablet_auto_run_2026
        /// <summary>
        /// API สำหรับกระตุ้นให้ระบบตรวจสอบและส่งอีเมลแจ้งเตือนไปยังแผนกต่างๆ
        /// </summary>
        /// <param name="key">รหัสผ่านสำหรับเข้าถึง API (เช่น tablet_auto_run_2026)</param>
        /// <returns>ผลลัพธ์การทำงานและจำนวนแผนกที่ถูกแจ้งเตือน</returns>
        [HttpGet]
        [Route("trigger")]
        public IHttpActionResult TriggerSend(string key)
        {
            if (key != ConfigurationManager.AppSettings["AlertApiKey"])
            {
                // ใช้ BadRequest แทน Json ของเดิม
                return BadRequest("Unauthorized access");
            }

            return ExecuteAlertLogic(DateTime.Now.Hour, null);
        }

        // URL: /api/alert/triggerCustom?key=tablet_auto_run_2026&dept=QA
        /// <summary>
        /// API สำหรับส่งอีเมลแจ้งเตือนแบบระบุแผนก (Custom Department)
        /// </summary>
        /// <param name="key">รหัสผ่านสำหรับเข้าถึง API</param>
        /// <param name="dept">ชื่อแผนกที่ต้องการส่ง (ใส่ได้หลายแผนกคั่นด้วยลูกน้ำ)</param>
        /// <param name="forceHour">จำลองเวลา (0, 8, 12, 20)</param>
        [HttpGet]
        [Route("triggerCustom")]
        public IHttpActionResult TriggerSendCustom(string key, string dept, int? forceHour)
        {
            if (key != ConfigurationManager.AppSettings["AlertApiKey"]) return BadRequest("Unauthorized access");
            if (string.IsNullOrEmpty(dept)) return BadRequest("Department parameter is required.");

            int hourToUse = forceHour ?? DateTime.Now.Hour;
            return ExecuteAlertLogic(hourToUse, dept);
        }

        // =========================================================
        // CORE LOGIC (ฟังก์ชันหลักที่อัปเดต SQL Query ใหม่, เพิ่ม Owner Name และแสดง Day/Night)
        // =========================================================
        private IHttpActionResult ExecuteAlertLogic(int hour, string deptFilter)
        {
            string targetSlot = "";
            bool includeDelay = false;
            string alertTypeStr = "";
            string emailSubjectPrefix = "";
            bool isSummaryMode = false;

            // 🌟 กำหนดเงื่อนไขตามรอบเวลาใหม่
            if (hour == 12)
            {
                targetSlot = "08:00-12:00";
                includeDelay = false;
                alertTypeStr = "แจ้งเตือน: อุปกรณ์ยังไม่ได้ Check-IN (กะเช้า)";
                emailSubjectPrefix = "[Alert] Missed Check-IN (Morning)";
            }
            else if (hour == 0)
            {
                targetSlot = "20:00-00:00";
                includeDelay = false;
                alertTypeStr = "แจ้งเตือน: อุปกรณ์ยังไม่ได้ Check-IN (กะดึก)";
                emailSubjectPrefix = "[Alert] Missed Check-IN (Night)";
            }
            else if (hour == 8)
            {
                // 🌟 รอบ 08:00: สรุปผลรวมของเมื่อวานทั้ง 2 กะ
                targetSlot = "08:00-12:00,20:00-00:00";
                includeDelay = true;
                isSummaryMode = true;
                alertTypeStr = "สรุปผลการ Check-IN ประจำวัน (รวมกะเช้า-กะดึก)";
                emailSubjectPrefix = "[Summary] Daily Report (Yesterday)";
            }
            else
            {
                return Content(HttpStatusCode.OK, new { status = "ignore", message = $"No tasks scheduled for hour: {hour}" });
            }

            // ถ้าเป็น Summary ตอน 8 โมง ให้ย้อนหลังไป 1 วันเสมอ
            DateTime bizDate = (isSummaryMode || (targetSlot.Contains("20:00-00:00") && hour <= 8))
                               ? DateTime.Now.Date.AddDays(-1) : DateTime.Now.Date;

            try
            {
                using (var conn = PostgreSqlDbConnection.GetConnection())
                {
                    // 🌟 SQL ปรับปรุงใหม่: สร้าง ExpectedShifts ตามการตั้งค่า check_morn และ check_night
                    string sql = @"
                        WITH ExpectedShifts AS (
                            -- ดึงเฉพาะเครื่องที่ต้องเช็คกะเช้า
                            SELECT d.asset_no, d.host_name, d.owner_name, d.dept_name, dept.email as dept_email,
                                   '08:00-12:00' AS expected_shift
                            FROM tablet_check_in.devices d
                            INNER JOIN tablet_check_in.departments dept ON d.dept_name = dept.dept_name
                            WHERE d.status = 'Delivered' 
                              AND d.check_morn = true
                              AND @slot ILIKE '%08:00-12:00%'
                              
                            UNION ALL
                            
                            -- ดึงเฉพาะเครื่องที่ต้องเช็คกะดึก
                            SELECT d.asset_no, d.host_name, d.owner_name, d.dept_name, dept.email as dept_email,
                                   '20:00-00:00' AS expected_shift
                            FROM tablet_check_in.devices d
                            INNER JOIN tablet_check_in.departments dept ON d.dept_name = dept.dept_name
                            WHERE d.status = 'Delivered' 
                              AND d.check_night = true
                              AND @slot ILIKE '%20:00-00:00%'
                        ),
                        RawData AS (
                            SELECT 
                                e.asset_no, e.host_name, e.owner_name, e.dept_name, e.dept_email,
                                e.expected_shift,
                                l.id as log_id,
                                CASE 
                                    WHEN l.id IS NULL THEN 'Missed'
                                    WHEN e.expected_shift = '08:00-12:00' THEN
                                        CASE WHEN l.checkin_time::time <= '12:00:59'::time THEN 'On-time' ELSE 'Delay' END
                                    WHEN e.expected_shift = '20:00-00:00' THEN
                                        CASE WHEN EXTRACT(HOUR FROM l.checkin_time) < 8 THEN 'Delay' ELSE 'On-time' END
                                    ELSE 'Unknown'
                                END as check_status
                            FROM ExpectedShifts e
                            -- นำกะที่คาดหวังมา LEFT JOIN กับ Log ที่เกิดขึ้นจริง
                            LEFT JOIN tablet_check_in.checkin_logs l 
                                ON e.asset_no = l.asset_no 
                                AND l.checkin_shift = e.expected_shift
                                AND (
                                    CASE WHEN l.checkin_shift = '20:00-00:00' AND EXTRACT(HOUR FROM l.checkin_time) < 8 
                                    THEN (l.checkin_time::date - INTERVAL '1 day')::date 
                                    ELSE l.checkin_time::date 
                                    END
                                ) = @bizDate
                        )
                        SELECT * FROM RawData WHERE 1=1 ";

                    if (includeDelay) sql += " AND check_status IN ('Missed', 'Delay') ";
                    else sql += " AND check_status = 'Missed' ";

                    if (!string.IsNullOrEmpty(deptFilter)) sql += " AND dept_name = ANY(string_to_array(@deptFilter, ',')) ";

                    sql += " ORDER BY dept_name, check_status DESC, asset_no, expected_shift";

                    var rawData = conn.Query(sql, new { slot = targetSlot, bizDate = bizDate, deptFilter = deptFilter }).ToList();

                    if (!rawData.Any()) return Content(HttpStatusCode.OK, new { status = "success", message = "No records found." });

                    var deptGroup = rawData.GroupBy(r => (string)r.dept_name).ToList();
                    int deptSentCount = 0;

                    foreach (var group in deptGroup)
                    {
                        string deptName = group.Key;
                        string targetEmail = group.First().dept_email;
                        if (string.IsNullOrEmpty(targetEmail)) continue;

                        StringBuilder sb = new StringBuilder();
                        sb.AppendLine($"เรียน ทีมงาน {deptName}\n");
                        sb.AppendLine($"เรื่อง {alertTypeStr}");
                        sb.AppendLine($"ประจำวันที่: {bizDate:dd/MM/yyyy}");
                        sb.AppendLine($"จำนวนอุปกรณ์ที่พบ: {group.Count()} เครื่อง\n");

                        sb.AppendLine("[ รายการอุปกรณ์ ]");
                        sb.AppendLine("----------------------------------------------------------------------------------------------------");
                        sb.AppendLine(string.Format("{0,-15} | {1,-20} | {2,-15} | {3,-11} | {4,-10}", "Asset No", "Host Name", "Owner Name", "Shift", "Status"));
                        sb.AppendLine("----------------------------------------------------------------------------------------------------");

                        foreach (var row in group)
                        {
                            string asset = row.asset_no ?? "";
                            string host = row.host_name ?? "";
                            string owner = row.owner_name ?? "-";

                            // 🌟 แปลงเวลาเป็น Day หรือ Night เพื่อแสดงผล
                            string rawShift = row.expected_shift ?? "";
                            string shiftDisplay = rawShift == "08:00-12:00" ? "Day" : (rawShift == "20:00-00:00" ? "Night" : rawShift);

                            string rStatus = row.check_status ?? "";

                            sb.AppendLine(string.Format("{0,-15} | {1,-20} | {2,-15} | {3,-11} | {4,-10}", asset, host, owner, shiftDisplay, rStatus));
                        }

                        sb.AppendLine("----------------------------------------------------------------------------------------------------");
                        sb.AppendLine("URL: http://htmfg2-postgre-test:92/Monitor");
                        sb.AppendLine("----------------------------------------------------------------------------------------------------\n");
                        sb.AppendLine("ขอแสดงความนับถือ\nระบบแจ้งเตือนอัตโนมัติ (Tablet Check-IN Center)");

                        SendEmail($"{emailSubjectPrefix} - {deptName} ({bizDate:dd/MM/yyyy})", "Tablet-Check-In@mail.canon", targetEmail, "lakkit@mail.canon;phuliwat@mail.canon;wasu@mail.canon", sb.ToString(), false);
                        deptSentCount++;
                    }
                    return Content(HttpStatusCode.OK, new { status = "success", departments_notified = deptSentCount });
                }
            }
            catch (Exception ex)
            {
                return Content(HttpStatusCode.InternalServerError, new { Message = ex.Message });
            }
        }

        // =========================================================
        // 4. Helper Function สำหรับส่ง Email
        // =========================================================
        private void SendEmail(string subject, string user_email, string target_email, string cc, string body, bool isHtml = false)
        {
            using (MailMessage mail = new MailMessage())
            {
                if (string.IsNullOrWhiteSpace(user_email)) throw new ArgumentException("Sender email cannot be empty.");

                mail.From = new MailAddress(user_email.Trim());
                mail.Subject = subject;
                mail.Body = body;
                mail.IsBodyHtml = isHtml;

                // เพิ่มผู้รับ (To)
                if (!string.IsNullOrWhiteSpace(target_email))
                {
                    var toAddresses = target_email.Split(new[] { ";", "," }, StringSplitOptions.RemoveEmptyEntries);
                    foreach (string to in toAddresses.Where(x => !string.IsNullOrWhiteSpace(x)))
                    {
                        mail.To.Add(new MailAddress(to.Trim()));
                    }
                }

                // เพิ่มสำเนา (CC)
                if (!string.IsNullOrWhiteSpace(cc))
                {
                    var ccAddresses = cc.Split(new[] { ";", "," }, StringSplitOptions.RemoveEmptyEntries);
                    foreach (string adr_cc in ccAddresses.Where(x => !string.IsNullOrWhiteSpace(x)))
                    {
                        mail.CC.Add(new MailAddress(adr_cc.Trim()));
                    }
                }

                // สั่งเชื่อมต่อ SMTP Server ของบริษัท
                using (SmtpClient client = new SmtpClient("nonauth-smtp.global.canon.co.jp", 25))
                {
                    client.DeliveryMethod = SmtpDeliveryMethod.Network;
                    client.UseDefaultCredentials = false;

                    // ส่งเมลก็ต่อเมื่อมีผู้รับอย่างน้อย 1 คน
                    if (mail.To.Count > 0 || mail.CC.Count > 0)
                    {
                        client.Send(mail);
                    }
                }
            }
        }
    }
}