using System.Reflection;
using DarkShot.Core;
using DarkShot.UI;

namespace DarkShot;

internal static class Program
{
    [STAThread]
    private static int Main(string[] args)
    {
        if (args.Any(a => a is "--help" or "-h" or "/?"))
        {
            WriteToConsole(CommandLineOptions.HelpText);
            return 0;
        }

        if (args.Any(a => a is "--version" or "-v"))
        {
            var version = Assembly.GetExecutingAssembly().GetName().Version?.ToString() ?? "1.0.0";
            WriteToConsole($"DarkShot {version}{Environment.NewLine}");
            return 0;
        }

        NativeMethods.TryEnablePerMonitorV2();
        try { Application.SetHighDpiMode(HighDpiMode.PerMonitorV2); } catch { /* manifest fallback */ }
        Application.EnableVisualStyles();
        Application.SetCompatibleTextRenderingDefault(false);
        Application.SetUnhandledExceptionMode(UnhandledExceptionMode.CatchException);

        Application.ThreadException += (_, e) =>
        {
            Log.Error(e.Exception, "Unhandled UI exception");
            MessageBox.Show("DarkShot hit an unexpected error:\n\n" + e.Exception.Message +
                            "\n\nDetails were written to:\n" + Log.FilePath,
                "DarkShot", MessageBoxButtons.OK, MessageBoxIcon.Error);
        };
        AppDomain.CurrentDomain.UnhandledException += (_, e) =>
        {
            Log.Error(e.ExceptionObject as Exception ?? new Exception("Unknown unhandled error"), "Unhandled exception");
        };
        TaskScheduler.UnobservedTaskException += (_, e) =>
        {
            Log.Error(e.Exception, "Unobserved task exception");
            e.SetObserved();
        };

        using var mutex = new Mutex(initiallyOwned: true, SingleInstance.MutexName, out bool createdNew);
        if (!createdNew)
        {
            if (SingleInstance.SendToRunningInstance(args))
                return 0;

            MessageBox.Show("DarkShot is already running, but the running instance did not respond.",
                "DarkShot", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            return 1;
        }

        AppSettings settings;
        try
        {
            settings = SettingsStore.Load();
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Settings load failed; using defaults");
            settings = new AppSettings();
            settings.Normalize();
        }

        TrayAppContext? context = null;
        SingleInstanceServer? server = null;
        try
        {
            context = new TrayAppContext(settings);
            server = new SingleInstanceServer();
            context.AttachIpc(server);

            if (args.Length > 0)
            {
                var startTimer = new System.Windows.Forms.Timer { Interval = 180 };
                startTimer.Tick += (_, _) =>
                {
                    startTimer.Stop();
                    startTimer.Dispose();
                    try { context.HandleCommand(args); }
                    catch (Exception ex) { Log.Error(ex, "Command line handling failed"); }
                };
                startTimer.Start();
            }

            Application.Run(context);
            return 0;
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Fatal startup error");
            MessageBox.Show("DarkShot could not start:\n\n" + ex.Message + "\n\nLog: " + Log.FilePath,
                "DarkShot", MessageBoxButtons.OK, MessageBoxIcon.Error);
            return 1;
        }
        finally
        {
            try { server?.Dispose(); } catch { /* ignore */ }
            try { context?.Dispose(); } catch { /* ignore */ }
            try { mutex.ReleaseMutex(); } catch { /* ignore */ }
        }
    }

    private static void WriteToConsole(string text)
    {
        try
        {
            if (NativeMethods.AttachConsole(unchecked((uint)-1)))
            {
                using var writer = new StreamWriter(Console.OpenStandardOutput()) { AutoFlush = true };
                writer.Write(text);
                NativeMethods.FreeConsole();
                return;
            }
        }
        catch { /* fall through */ }

        try
        {
            MessageBox.Show(text, "DarkShot", MessageBoxButtons.OK, MessageBoxIcon.Information);
        }
        catch { /* ignore */ }
    }
}
