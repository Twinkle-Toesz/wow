using System;
using System.Threading;
using System.Windows.Forms;

namespace TabbedRdp
{
    internal static class Program
    {
        [STAThread]
        private static void Main(string[] args)
        {
            Application.EnableVisualStyles();
            Application.SetCompatibleTextRenderingDefault(false);
            Application.SetUnhandledExceptionMode(UnhandledExceptionMode.CatchException);
            Application.ThreadException += OnThreadException;
            Application.Run(new MainForm(args));
        }

        private static void OnThreadException(object sender, ThreadExceptionEventArgs e)
        {
            MessageBox.Show(e.Exception.Message, "Tabbed RDP - unexpected error", MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
    }
}
