using System;
using System.Diagnostics;
using System.Net.Http;
using System.Threading.Tasks;
using Config;

namespace Service
{
    public static class WindowsServiceManager
    {
        public static void InstallService(string exePath)
        {
            Console.WriteLine($"Installing Windows Service '{BuildInfo.ServiceName}'...");
            RunScCommand($"create {BuildInfo.ServiceName} binPath= \"{exePath}\" start= auto DisplayName= \"{BuildInfo.ServiceName}\"");
            RunScCommand($"description {BuildInfo.ServiceName} \"{BuildInfo.ServiceDescription}\"");
            
            // Cookie Standard #13: 3 auto-restart attempts (5s, 10s, 30s)
            RunScCommand($"failure {BuildInfo.ServiceName} reset= 86400 actions= restart/5000/restart/10000/restart/30000");
            
            Console.WriteLine($"Service '{BuildInfo.ServiceName}' installed successfully with auto-recovery.");
        }

        public static void RemoveService()
        {
            Console.WriteLine($"Removing Windows Service '{BuildInfo.ServiceName}'...");
            RunScCommand($"stop {BuildInfo.ServiceName}");
            RunScCommand($"delete {BuildInfo.ServiceName}");
            Console.WriteLine($"Service '{BuildInfo.ServiceName}' removed.");
        }

        public static void StartService()
        {
            Console.WriteLine($"Starting Windows Service '{BuildInfo.ServiceName}'...");
            RunScCommand($"start {BuildInfo.ServiceName}");
        }

        public static void StopService()
        {
            Console.WriteLine($"Stopping Windows Service '{BuildInfo.ServiceName}'...");
            RunScCommand($"stop {BuildInfo.ServiceName}");
        }

        public static async Task QueryStatusAsync()
        {
            try
            {
                using var client = new HttpClient { Timeout = TimeSpan.FromSeconds(3) };
                string json = await client.GetStringAsync($"http://localhost:{BuildInfo.DefaultPort}/status");
                Console.WriteLine($"HTTP : {BuildInfo.DefaultPort} Status:\n{json}");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"HTTP Status query failed ({ex.Message}). Querying SCM...");
                RunScCommand($"query {BuildInfo.ServiceName}");
            }
        }

        private static void RunScCommand(string args)
        {
            try
            {
                var psi = new ProcessStartInfo
                {
                    FileName = "sc.exe",
                    Arguments = args,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    UseShellExecute = false,
                    CreateNoWindow = true
                };

                using var proc = Process.Start(psi);
                if (proc != null)
                {
                    proc.WaitForExit(5000);
                    string output = proc.StandardOutput.ReadToEnd();
                    string error = proc.StandardError.ReadToEnd();
                    if (!string.IsNullOrWhiteSpace(output)) Console.WriteLine(output.Trim());
                    if (!string.IsNullOrWhiteSpace(error)) Console.WriteLine($"ERROR: {error.Trim()}");
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Failed to execute sc.exe {args}: {ex.Message}");
            }
        }
    }
}
