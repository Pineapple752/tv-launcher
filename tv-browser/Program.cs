namespace TVBrowser;

static class Program
{
    [STAThread]
    static void Main(string[] args)
    {
        // only one copy at a time
        using var mutex = new Mutex(true, "TVBrowser-single-instance", out bool isFirst);
        if (!isFirst)
        {
            // a copy started by the crash-recovery restart waits for the old one to finish closing
            if (!args.Contains("--restart")) return;
            try { if (!mutex.WaitOne(10000)) return; }
            catch (AbandonedMutexException) { /* the old copy died; we now own the mutex */ }
        }

        ApplicationConfiguration.Initialize();
        Application.Run(new MainForm());
    }
}
