using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace NewsForm
{
    internal static class Program
    {
        /// <summary>
        /// The main entry point for the application.
        /// </summary>
        [STAThread]
        static void Main(string[] args)
        {
            Application.EnableVisualStyles();
            Application.SetCompatibleTextRenderingDefault(false);
            try
            {
                if (args.Length == 2 && args[0] == "--request")
                    Application.Run(new SectorNewsForm(SectorNewsRequest.Read(args[1])));
                else if (args.Length == 0) Application.Run(new Form1());
                else throw new ArgumentException("Usage: NewsForm.exe [--request file.json]");
            }
            catch (Exception ex) { MessageBox.Show(ex.Message, "뉴스 실행 오류", MessageBoxButtons.OK, MessageBoxIcon.Error); }
        }
    }
}
