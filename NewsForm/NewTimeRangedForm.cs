using System;
using System.Drawing;
using System.Windows.Forms;

namespace New_Tradegy.Library.Utils
{
    public partial class NewsTimeRangeForm : Form
    {
        private readonly DateTimePicker _start;
        private readonly DateTimePicker _end;

        public DateTime StartTime => _start.Value;
        public DateTime EndTime => _end.Value;

        public NewsTimeRangeForm()
        {
            InitializeComponent();

            Text = "뉴스 수집 시간";
            StartPosition = FormStartPosition.CenterScreen;
            FormBorderStyle = FormBorderStyle.FixedDialog;
            MaximizeBox = false;
            MinimizeBox = false;

            ClientSize = new Size(430, 155);
            Font = new Font("맑은 고딕", 11F);

            var lblStart = new Label
            {
                Text = "시작",
                Location = new Point(20, 24),
                AutoSize = true
            };

            var lblEnd = new Label
            {
                Text = "종료",
                Location = new Point(20, 66),
                AutoSize = true
            };

            _start = new DateTimePicker
            {
                Location = new Point(80, 20),
                Size = new Size(320, 30),
                Format = DateTimePickerFormat.Custom,
                CustomFormat = "yyyy/MM/dd HH:mm",
                ShowUpDown = true,
                Value = DateTime.Today
                    .AddDays(-1)
                    .AddHours(9)
            };

            _end = new DateTimePicker
            {
                Location = new Point(80, 62),
                Size = new Size(320, 30),
                Format = DateTimePickerFormat.Custom,
                CustomFormat = "yyyy/MM/dd HH:mm",
                ShowUpDown = true,
                Value = DateTime.Now
            };

            var btnOk = new Button
            {
                Text = "수집",
                Location = new Point(230, 108),
                Size = new Size(80, 32),
                DialogResult = DialogResult.OK
            };

            var btnCancel = new Button
            {
                Text = "취소",
                Location = new Point(320, 108),
                Size = new Size(80, 32),
                DialogResult = DialogResult.Cancel
            };

            Controls.Add(lblStart);
            Controls.Add(lblEnd);
            Controls.Add(_start);
            Controls.Add(_end);
            Controls.Add(btnOk);
            Controls.Add(btnCancel);

            AcceptButton = btnOk;
            CancelButton = btnCancel;
        }
    }
}