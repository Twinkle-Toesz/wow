using System;
using System.IO.Pipes;
using System.Runtime.Serialization.Json;
using System.Security.Principal;
using System.Threading;
using System.Windows.Forms;

namespace TabbedRdp
{
    internal static class Program
    {
        [STAThread]
        private static void Main(string[] args)
        {
            Log.Write("start: " + LaunchParser.Mask(Environment.CommandLine));
            var launch = LaunchParser.Parse(args);

            // Things only the real client can do (shadowing, all monitors, RemoteApp…) go straight to mstsc.exe.
            if (launch.DelegateReason != null)
            {
                Log.Write($"  {launch.DelegateReason} is not supported by the embedded control");
                try { LaunchParser.RunMstsc(args); return; }
                catch (Exception ex) { launch.Errors.Add("Could not start mstsc.exe: " + ex.Message); }
            }

            string id = WindowsIdentity.GetCurrent().User?.Value ?? Environment.UserName;
            string pipeName = "TabbedRDP-" + id;

            using (var mutex = new Mutex(true, @"Local\TabbedRDP-" + id, out bool firstInstance))
            {
                // One window: further launches (e.g. from a web connector) open as new tabs in it.
                if (!firstInstance && !launch.Request.NewWindow && launch.Errors.Count == 0 && TrySend(pipeName, launch.Request))
                {
                    Log.Write($"  sent {launch.Request.Connections.Count} connection(s) to the running window");
                    return;
                }

                Application.EnableVisualStyles();
                Application.SetCompatibleTextRenderingDefault(false);
                Application.SetUnhandledExceptionMode(UnhandledExceptionMode.CatchException);
                Application.ThreadException += OnThreadException;

                var form = new MainForm(launch);
                if (firstInstance) form.Shown += (s, e) => Listen(pipeName, form);
                Application.Run(form);
                GC.KeepAlive(mutex);
            }
        }

        private static bool TrySend(string pipeName, LaunchRequest request)
        {
            try
            {
                using (var client = new NamedPipeClientStream(".", pipeName, PipeDirection.Out))
                {
                    client.Connect(5000);
                    new DataContractJsonSerializer(typeof(LaunchRequest)).WriteObject(client, request);
                    client.Flush();
                }
                return true;
            }
            catch (Exception ex)
            {
                Log.Write("  could not reach the running window: " + ex.Message);
                return false;
            }
        }

        private static void Listen(string pipeName, MainForm form)
        {
            var thread = new Thread(() =>
            {
                while (!form.IsDisposed)
                {
                    try
                    {
                        // Default pipe security: only this user (and admins/SYSTEM) can write to it.
                        using (var server = new NamedPipeServerStream(pipeName, PipeDirection.In, 1))
                        {
                            server.WaitForConnection();
                            var request = (LaunchRequest)new DataContractJsonSerializer(typeof(LaunchRequest)).ReadObject(server);
                            if (request != null && !form.IsDisposed)
                                form.BeginInvoke((Action)(() => form.OpenRequest(request)));
                        }
                    }
                    catch (Exception ex)
                    {
                        Log.Write("  pipe: " + ex.Message);
                        Thread.Sleep(250);
                    }
                }
            }) { IsBackground = true, Name = "TabbedRDP launch listener" };
            thread.Start();
        }

        private static void OnThreadException(object sender, ThreadExceptionEventArgs e)
        {
            MessageBox.Show(e.Exception.Message, "Tabbed RDP - unexpected error", MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
    }
}
