using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Windows.Forms;

// ============================================================
//  Smart_NetID  –  expanded version of hollykhk/smart-id
//  Original: console-based ID card / time-recording program
//  Added   : Windows Forms GUI, persistent CSV log, search,
//            check-in/check-out tracking, admin panel
// ============================================================

namespace SmartNetID
{
    // ── Data model ──────────────────────────────────────────
    public class Student
    {
        public string NetID      { get; set; }
        public string FirstName  { get; set; }
        public string LastName   { get; set; }
        public string Department { get; set; }

        public override string ToString() =>
            $"{NetID} | {FirstName} {LastName} | {Department}";
    }

    public class AttendanceRecord
    {
        public string   NetID     { get; set; }
        public DateTime CheckIn   { get; set; }
        public DateTime? CheckOut { get; set; }

        public string Duration =>
            CheckOut.HasValue
                ? (CheckOut.Value - CheckIn).ToString(@"hh\:mm\:ss")
                : "Still checked in";
    }

    // ── Persistence helpers ──────────────────────────────────
    public static class DataStore
    {
        private const string StudentsFile   = "students.csv";
        private const string AttendanceFile = "attendance.csv";

        // ---------- students ----------
        public static List<Student> LoadStudents()
        {
            var list = new List<Student>();
            if (!File.Exists(StudentsFile)) return list;
            foreach (var line in File.ReadAllLines(StudentsFile).Skip(1))
            {
                var p = line.Split(',');
                if (p.Length < 4) continue;
                list.Add(new Student {
                    NetID      = p[0].Trim(),
                    FirstName  = p[1].Trim(),
                    LastName   = p[2].Trim(),
                    Department = p[3].Trim()
                });
            }
            return list;
        }

        public static void SaveStudents(List<Student> students)
        {
            var lines = new List<string> { "NetID,FirstName,LastName,Department" };
            lines.AddRange(students.Select(s =>
                $"{s.NetID},{s.FirstName},{s.LastName},{s.Department}"));
            File.WriteAllLines(StudentsFile, lines);
        }

        // ---------- attendance ----------
        public static List<AttendanceRecord> LoadAttendance()
        {
            var list = new List<AttendanceRecord>();
            if (!File.Exists(AttendanceFile)) return list;
            foreach (var line in File.ReadAllLines(AttendanceFile).Skip(1))
            {
                var p = line.Split(',');
                if (p.Length < 3) continue;
                list.Add(new AttendanceRecord {
                    NetID    = p[0].Trim(),
                    CheckIn  = DateTime.Parse(p[1].Trim()),
                    CheckOut = string.IsNullOrWhiteSpace(p[2]) ? (DateTime?)null
                                                                : DateTime.Parse(p[2].Trim())
                });
            }
            return list;
        }

        public static void SaveAttendance(List<AttendanceRecord> records)
        {
            var lines = new List<string> { "NetID,CheckIn,CheckOut" };
            lines.AddRange(records.Select(r =>
                $"{r.NetID},{r.CheckIn:yyyy-MM-dd HH:mm:ss},{r.CheckOut?.ToString("yyyy-MM-dd HH:mm:ss") ?? ""}"));
            File.WriteAllLines(AttendanceFile, lines);
        }
    }

    // ── Main application window ──────────────────────────────
    public class MainForm : Form
    {
        // ---- data ----
        private List<Student>          _students   = DataStore.LoadStudents();
        private List<AttendanceRecord> _attendance = DataStore.LoadAttendance();

        // ---- controls ----
        private TabControl   tabs;
        private TabPage      tabCheckIn, tabRoster, tabLog, tabAdmin;

        // Check-In tab
        private TextBox      txtScan;
        private Label        lblStatus;
        private ListBox      lstActive;

        // Roster tab
        private DataGridView dgvRoster;
        private TextBox      txtSearch;

        // Log tab
        private DataGridView dgvLog;
        private DateTimePicker dtpFilter;
        private Button       btnExport;

        // Admin tab
        private TextBox      txtNewNetID, txtNewFirst, txtNewLast, txtNewDept;
        private Button       btnAddStudent, btnDeleteStudent;
        private Label        lblAdminMsg;

        public MainForm()
        {
            InitUI();
            RefreshAll();
        }

        // ── UI construction ──────────────────────────────────
        private void InitUI()
        {
            Text            = "Smart NetID System";
            Size            = new Size(860, 600);
            MinimumSize     = new Size(720, 500);
            StartPosition   = FormStartPosition.CenterScreen;
            BackColor       = Color.FromArgb(30, 30, 40);
            ForeColor       = Color.WhiteSmoke;
            Font            = new Font("Segoe UI", 9f);

            // ── tab strip ────────────────────────────────────
            tabs = new TabControl { Dock = DockStyle.Fill };
            tabs.Appearance = TabAppearance.FlatButtons;
            tabs.DrawMode   = TabDrawMode.OwnerDrawFixed;
            tabs.DrawItem  += Tabs_DrawItem;

            tabCheckIn = new TabPage("  CHECK-IN / OUT  ");
            tabRoster  = new TabPage("  ROSTER  ");
            tabLog     = new TabPage("  ATTENDANCE LOG  ");
            tabAdmin   = new TabPage("  ADMIN  ");

            foreach (var tp in new[] { tabCheckIn, tabRoster, tabLog, tabAdmin })
            {
                tp.BackColor = Color.FromArgb(40, 40, 55);
                tp.ForeColor = Color.WhiteSmoke;
                tabs.TabPages.Add(tp);
            }

            // ── Check-In tab ─────────────────────────────────
            var pnlScan = new Panel { Dock = DockStyle.Top, Height = 90, Padding = new Padding(10) };
            pnlScan.BackColor = Color.FromArgb(50, 50, 70);

            var lblPrompt = new Label {
                Text = "Scan or type your NetID, then press Enter:",
                AutoSize = true, Location = new Point(10, 12), ForeColor = Color.Silver
            };

            txtScan = new TextBox {
                Location = new Point(10, 34), Width = 260,
                BackColor = Color.FromArgb(20, 20, 30), ForeColor = Color.Lime,
                Font = new Font("Consolas", 13f), BorderStyle = BorderStyle.FixedSingle
            };
            txtScan.KeyDown += TxtScan_KeyDown;

            lblStatus = new Label {
                Location = new Point(280, 34), AutoSize = true,
                ForeColor = Color.Yellow, Font = new Font("Segoe UI", 10f, FontStyle.Bold)
            };

            pnlScan.Controls.AddRange(new Control[] { lblPrompt, txtScan, lblStatus });

            var lblActive = new Label {
                Text = "Currently Checked In:", Dock = DockStyle.Top,
                Height = 24, ForeColor = Color.Silver,
                Padding = new Padding(6, 4, 0, 0)
            };
            lstActive = new ListBox {
                Dock = DockStyle.Fill,
                BackColor = Color.FromArgb(25, 25, 35), ForeColor = Color.LightGreen,
                Font = new Font("Consolas", 9f), BorderStyle = BorderStyle.None
            };

            tabCheckIn.Controls.Add(lstActive);
            tabCheckIn.Controls.Add(lblActive);
            tabCheckIn.Controls.Add(pnlScan);

            // ── Roster tab ───────────────────────────────────
            var pnlSearch = new Panel { Dock = DockStyle.Top, Height = 42, Padding = new Padding(8, 8, 8, 0) };
            pnlSearch.BackColor = Color.FromArgb(50, 50, 70);
            var lblFind = new Label { Text = "Search: ", AutoSize = true, Location = new Point(8, 12), ForeColor = Color.Silver };
            txtSearch = new TextBox {
                Location = new Point(60, 8), Width = 240,
                BackColor = Color.FromArgb(20, 20, 30), ForeColor = Color.WhiteSmoke
            };
            txtSearch.TextChanged += (s, e) => RefreshRoster();
            pnlSearch.Controls.AddRange(new Control[] { lblFind, txtSearch });

            dgvRoster = BuildGrid();
            dgvRoster.Columns.Add("NetID",      "NetID");
            dgvRoster.Columns.Add("FirstName",  "First Name");
            dgvRoster.Columns.Add("LastName",   "Last Name");
            dgvRoster.Columns.Add("Department", "Department");
            dgvRoster.Dock = DockStyle.Fill;

            tabRoster.Controls.Add(dgvRoster);
            tabRoster.Controls.Add(pnlSearch);

            // ── Log tab ──────────────────────────────────────
            var pnlLogBar = new Panel { Dock = DockStyle.Top, Height = 42, Padding = new Padding(8, 8, 8, 0) };
            pnlLogBar.BackColor = Color.FromArgb(50, 50, 70);
            var lblDate = new Label { Text = "Filter date:", AutoSize = true, Location = new Point(8, 12), ForeColor = Color.Silver };
            dtpFilter = new DateTimePicker {
                Format = DateTimePickerFormat.Short, Location = new Point(80, 8), Width = 120,
                BackColor = Color.FromArgb(20, 20, 30), ForeColor = Color.WhiteSmoke, CalendarForeColor = Color.Black
            };
            dtpFilter.ValueChanged += (s, e) => RefreshLog();
            btnExport = StyledButton("Export CSV", new Point(220, 8));
            btnExport.Click += BtnExport_Click;
            pnlLogBar.Controls.AddRange(new Control[] { lblDate, dtpFilter, btnExport });

            dgvLog = BuildGrid();
            dgvLog.Columns.Add("NetID",    "NetID");
            dgvLog.Columns.Add("Name",     "Full Name");
            dgvLog.Columns.Add("CheckIn",  "Check In");
            dgvLog.Columns.Add("CheckOut", "Check Out");
            dgvLog.Columns.Add("Duration", "Duration");
            dgvLog.Dock = DockStyle.Fill;

            tabLog.Controls.Add(dgvLog);
            tabLog.Controls.Add(pnlLogBar);

            // ── Admin tab ────────────────────────────────────
            var pnlAdmin = new Panel { Dock = DockStyle.Fill, Padding = new Padding(20) };

            int y = 20;
            pnlAdmin.Controls.Add(AdminLabel("NetID:",      20, y));
            txtNewNetID = AdminBox(120, y); pnlAdmin.Controls.Add(txtNewNetID); y += 34;

            pnlAdmin.Controls.Add(AdminLabel("First Name:", 20, y));
            txtNewFirst = AdminBox(120, y); pnlAdmin.Controls.Add(txtNewFirst); y += 34;

            pnlAdmin.Controls.Add(AdminLabel("Last Name:",  20, y));
            txtNewLast  = AdminBox(120, y); pnlAdmin.Controls.Add(txtNewLast);  y += 34;

            pnlAdmin.Controls.Add(AdminLabel("Department:", 20, y));
            txtNewDept  = AdminBox(120, y); pnlAdmin.Controls.Add(txtNewDept);  y += 44;

            btnAddStudent    = StyledButton("Add Student",    new Point(20, y));
            btnDeleteStudent = StyledButton("Delete by NetID", new Point(140, y));
            btnDeleteStudent.BackColor = Color.FromArgb(140, 40, 40);
            btnAddStudent.Click    += BtnAdd_Click;
            btnDeleteStudent.Click += BtnDelete_Click;
            pnlAdmin.Controls.Add(btnAddStudent);
            pnlAdmin.Controls.Add(btnDeleteStudent);

            lblAdminMsg = new Label {
                Location = new Point(20, y + 40), AutoSize = true,
                ForeColor = Color.Yellow, Font = new Font("Segoe UI", 9f, FontStyle.Italic)
            };
            pnlAdmin.Controls.Add(lblAdminMsg);

            tabAdmin.Controls.Add(pnlAdmin);

            // ── assemble ─────────────────────────────────────
            Controls.Add(tabs);

            // status bar
            var bar = new StatusStrip();
            bar.BackColor = Color.FromArgb(20, 20, 30);
            var lbl = new ToolStripStatusLabel($"Smart NetID System  |  {DateTime.Now:ddd, MMM d yyyy}");
            lbl.ForeColor = Color.DimGray;
            bar.Items.Add(lbl);
            Controls.Add(bar);
        }

        // ── Helpers ──────────────────────────────────────────
        private DataGridView BuildGrid()
        {
            var g = new DataGridView {
                AllowUserToAddRows    = false,
                AllowUserToDeleteRows = false,
                ReadOnly              = true,
                SelectionMode         = DataGridViewSelectionMode.FullRowSelect,
                BackgroundColor       = Color.FromArgb(25, 25, 35),
                GridColor             = Color.FromArgb(60, 60, 80),
                BorderStyle           = BorderStyle.None,
                RowHeadersVisible     = false,
                AutoSizeColumnsMode   = DataGridViewAutoSizeColumnsMode.Fill,
                ColumnHeadersHeightSizeMode = DataGridViewColumnHeadersHeightSizeMode.DisableResizing
            };
            g.DefaultCellStyle.BackColor      = Color.FromArgb(30, 30, 45);
            g.DefaultCellStyle.ForeColor      = Color.WhiteSmoke;
            g.DefaultCellStyle.SelectionBackColor = Color.FromArgb(70, 100, 160);
            g.DefaultCellStyle.SelectionForeColor = Color.White;
            g.ColumnHeadersDefaultCellStyle.BackColor = Color.FromArgb(50, 50, 75);
            g.ColumnHeadersDefaultCellStyle.ForeColor = Color.Silver;
            g.AlternatingRowsDefaultCellStyle.BackColor = Color.FromArgb(35, 35, 52);
            return g;
        }

        private Button StyledButton(string text, Point loc)
        {
            return new Button {
                Text = text, Location = loc, Width = 110, Height = 28,
                BackColor = Color.FromArgb(60, 100, 160), ForeColor = Color.White,
                FlatStyle = FlatStyle.Flat, Cursor = Cursors.Hand
            };
        }

        private Label AdminLabel(string text, int x, int y) =>
            new Label { Text = text, Location = new Point(x, y + 3), AutoSize = true, ForeColor = Color.Silver };

        private TextBox AdminBox(int x, int y) =>
            new TextBox {
                Location = new Point(x, y), Width = 200,
                BackColor = Color.FromArgb(20, 20, 30), ForeColor = Color.WhiteSmoke,
                BorderStyle = BorderStyle.FixedSingle
            };

        private void Tabs_DrawItem(object sender, DrawItemEventArgs e)
        {
            var page  = tabs.TabPages[e.Index];
            var brush = e.Index == tabs.SelectedIndex
                        ? new SolidBrush(Color.FromArgb(70, 100, 160))
                        : new SolidBrush(Color.FromArgb(40, 40, 60));
            e.Graphics.FillRectangle(brush, e.Bounds);
            e.Graphics.DrawString(page.Text, new Font("Segoe UI", 8.5f, FontStyle.Bold),
                                  Brushes.WhiteSmoke, e.Bounds, new StringFormat {
                                      Alignment = StringAlignment.Center,
                                      LineAlignment = StringAlignment.Center
                                  });
        }

        // ── Logic ────────────────────────────────────────────
        private void TxtScan_KeyDown(object sender, KeyEventArgs e)
        {
            if (e.KeyCode != Keys.Enter) return;
            e.SuppressKeyPress = true;

            string id = txtScan.Text.Trim().ToUpper();
            txtScan.Clear();

            if (string.IsNullOrEmpty(id)) return;

            var student = _students.FirstOrDefault(s =>
                s.NetID.Equals(id, StringComparison.OrdinalIgnoreCase));

            if (student == null)
            {
                lblStatus.ForeColor = Color.OrangeRed;
                lblStatus.Text      = $"❌  NetID '{id}' not found.";
                return;
            }

            // Is there an open (no check-out) record?
            var open = _attendance.LastOrDefault(r =>
                r.NetID.Equals(id, StringComparison.OrdinalIgnoreCase) && !r.CheckOut.HasValue);

            if (open != null)
            {
                // CHECK OUT
                open.CheckOut     = DateTime.Now;
                lblStatus.ForeColor = Color.LightBlue;
                lblStatus.Text    = $"✔  {student.FirstName} checked OUT at {open.CheckOut:HH:mm:ss}";
            }
            else
            {
                // CHECK IN
                _attendance.Add(new AttendanceRecord { NetID = id, CheckIn = DateTime.Now });
                lblStatus.ForeColor = Color.LimeGreen;
                lblStatus.Text      = $"✔  {student.FirstName} checked IN at {DateTime.Now:HH:mm:ss}";
            }

            DataStore.SaveAttendance(_attendance);
            RefreshAll();
        }

        private void BtnAdd_Click(object sender, EventArgs e)
        {
            string id   = txtNewNetID.Text.Trim().ToUpper();
            string fn   = txtNewFirst.Text.Trim();
            string ln   = txtNewLast.Text.Trim();
            string dept = txtNewDept.Text.Trim();

            if (string.IsNullOrEmpty(id) || string.IsNullOrEmpty(fn) || string.IsNullOrEmpty(ln))
            {
                lblAdminMsg.ForeColor = Color.OrangeRed;
                lblAdminMsg.Text = "NetID, first name and last name are required.";
                return;
            }

            if (_students.Any(s => s.NetID.Equals(id, StringComparison.OrdinalIgnoreCase)))
            {
                lblAdminMsg.ForeColor = Color.OrangeRed;
                lblAdminMsg.Text = $"NetID '{id}' already exists.";
                return;
            }

            _students.Add(new Student { NetID = id, FirstName = fn, LastName = ln, Department = dept });
            DataStore.SaveStudents(_students);

            lblAdminMsg.ForeColor = Color.LimeGreen;
            lblAdminMsg.Text = $"Student '{id}' added successfully.";
            txtNewNetID.Clear(); txtNewFirst.Clear(); txtNewLast.Clear(); txtNewDept.Clear();
            RefreshRoster();
        }

        private void BtnDelete_Click(object sender, EventArgs e)
        {
            string id = txtNewNetID.Text.Trim().ToUpper();
            var s = _students.FirstOrDefault(x => x.NetID.Equals(id, StringComparison.OrdinalIgnoreCase));
            if (s == null)
            {
                lblAdminMsg.ForeColor = Color.OrangeRed;
                lblAdminMsg.Text = $"NetID '{id}' not found.";
                return;
            }
            _students.Remove(s);
            DataStore.SaveStudents(_students);
            lblAdminMsg.ForeColor = Color.Yellow;
            lblAdminMsg.Text = $"Student '{id}' removed.";
            RefreshRoster();
        }

        private void BtnExport_Click(object sender, EventArgs e)
        {
            var sfd = new SaveFileDialog {
                Filter = "CSV files|*.csv", FileName = $"attendance_{DateTime.Now:yyyyMMdd}.csv"
            };
            if (sfd.ShowDialog() != DialogResult.OK) return;
            var lines = new List<string> { "NetID,FullName,CheckIn,CheckOut,Duration" };
            foreach (DataGridViewRow row in dgvLog.Rows)
            {
                lines.Add(string.Join(",", row.Cells.Cast<DataGridViewCell>().Select(c => c.Value?.ToString() ?? "")));
            }
            File.WriteAllLines(sfd.FileName, lines);
            MessageBox.Show("Exported successfully.", "Export", MessageBoxButtons.OK, MessageBoxIcon.Information);
        }

        // ── Refresh helpers ───────────────────────────────────
        private void RefreshAll()
        {
            RefreshActive();
            RefreshRoster();
            RefreshLog();
        }

        private void RefreshActive()
        {
            lstActive.Items.Clear();
            var active = _attendance
                .Where(r => !r.CheckOut.HasValue)
                .Select(r => {
                    var s = _students.FirstOrDefault(x =>
                        x.NetID.Equals(r.NetID, StringComparison.OrdinalIgnoreCase));
                    string name = s != null ? $"{s.FirstName} {s.LastName}" : "Unknown";
                    return $"{r.NetID}  –  {name}  (in at {r.CheckIn:HH:mm})";
                });
            foreach (var line in active)
                lstActive.Items.Add(line);
        }

        private void RefreshRoster()
        {
            dgvRoster.Rows.Clear();
            string q = txtSearch?.Text.Trim().ToLower() ?? "";
            var filtered = string.IsNullOrEmpty(q)
                ? _students
                : _students.Where(s =>
                    s.NetID.ToLower().Contains(q) ||
                    s.FirstName.ToLower().Contains(q) ||
                    s.LastName.ToLower().Contains(q) ||
                    s.Department.ToLower().Contains(q)).ToList();
            foreach (var s in filtered)
                dgvRoster.Rows.Add(s.NetID, s.FirstName, s.LastName, s.Department);
        }

        private void RefreshLog()
        {
            dgvLog.Rows.Clear();
            var date = dtpFilter?.Value.Date ?? DateTime.Today;
            var filtered = _attendance.Where(r => r.CheckIn.Date == date);
            foreach (var r in filtered)
            {
                var s = _students.FirstOrDefault(x =>
                    x.NetID.Equals(r.NetID, StringComparison.OrdinalIgnoreCase));
                string name = s != null ? $"{s.FirstName} {s.LastName}" : "Unknown";
                dgvLog.Rows.Add(
                    r.NetID, name,
                    r.CheckIn.ToString("HH:mm:ss"),
                    r.CheckOut?.ToString("HH:mm:ss") ?? "—",
                    r.Duration
                );
            }
        }

        // ── Entry point ───────────────────────────────────────
        [STAThread]
        public static void Main()
        {
            Application.EnableVisualStyles();
            Application.SetCompatibleTextRenderingDefault(false);
            Application.Run(new MainForm());
        }
    }
}
