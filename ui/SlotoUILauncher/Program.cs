using System;
using System.Diagnostics;
using System.IO;
using System.Reflection;

class Program
{
    static void Main()
    {
        Console.WriteLine("Starting SaaS Engine UI...");

        // Try to figure out where we are relative to the project
        // So the exe works even if they move it around a bit, but we fallback to absolute path if needed
        string uiDir = @"K:\Work\Project\Sloto\ui";
        string frontendDir = @"K:\Work\Project\Sloto\ui\frontend";

        try
        {
            var serverProcess = new Process
            {
                StartInfo = new ProcessStartInfo
                {
                    FileName = "cmd.exe",
                    Arguments = $"/c title Command Bridge Server && node server.js",
                    WorkingDirectory = uiDir,
                    UseShellExecute = true
                }
            };

            var frontendProcess = new Process
            {
                StartInfo = new ProcessStartInfo
                {
                    FileName = "cmd.exe",
                    Arguments = $"/k title SaaS Engine UI && npm run dev",
                    WorkingDirectory = frontendDir,
                    UseShellExecute = true
                }
            };

            serverProcess.Start();
            frontendProcess.Start();

            Console.WriteLine("Launched successfully!");
        }
        catch (Exception ex)
        {
            Console.WriteLine("Error launching UI: " + ex.Message);
            Console.ReadLine();
        }
    }
}
