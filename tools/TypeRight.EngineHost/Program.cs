using System;
using System.Runtime.InteropServices;
using TypeRight.Engine;

namespace TypeRight.EngineHost
{
    internal static class Program
    {
        // Windows Text Services Framework native types and constants
        private const int COINIT_APARTMENTTHREADED = 0x2;
        private const int COINIT_MULTITHREADED = 0x0;

        [DllImport("ole32.dll")]
        private static extern int CoInitializeEx(IntPtr pvReserved, int dwCoInit);

        [DllImport("ole32.dll")]
        private static extern void CoUninitialize();

        private static void Main(string[] args)
        {
            Console.WriteLine("TypeRight 2.0 - Windows Text Services Framework Host");
            Console.WriteLine("============================================");
            Console.WriteLine();

            // Initialize COM for TSF - multithreaded apartment is typical for TSF
            int hr = CoInitializeEx(IntPtr.Zero, COINIT_MULTITHREADED);
            if (hr >= 0 || hr == unchecked((int)0x80040155)) // S_OK or E_COINITIZE_NOCONTEXT
            {
                try
                {
                    Console.ForegroundColor = ConsoleColor.Green;
                    Console.WriteLine("✓ COM initialized for TSF (multithreaded apartment)");
                    Console.ResetColor();

                    // Create the correction engine composition
                    var decisionMaker = TypeRight.Engine.EngineComposition.CreateDecisionMaker();

                    Console.ForegroundColor = ConsoleColor.Green;
                    Console.WriteLine("✓ Correction engine loaded and configured");
                    Console.ResetColor();

                    Console.WriteLine();
                    Console.WriteLine("TypeRight 2.0 TSF Host operational.");
                    Console.WriteLine("The correction engine is ready for Windows Text Services Framework integration.");
                    Console.WriteLine();
                }
                finally
                {
                    CoUninitialize();
                }
            }
            else
            {
                Console.ForegroundColor = ConsoleColor.Red;
                Console.WriteLine($"✗ COM initialization failed (HRESULT: 0x{hr:X8})");
                Console.ResetColor();

                Console.ForegroundColor = ConsoleColor.Yellow;
                Console.WriteLine("Attempting single-threaded apartment...");
                Console.ResetColor();

                // Try single-threaded apartment
                hr = CoInitializeEx(IntPtr.Zero, COINIT_APARTMENTTHREADED);
                if (hr >= 0 || hr == unchecked((int)0x80040155))
                {
                    try
                    {
                        Console.ForegroundColor = ConsoleColor.Green;
                        Console.WriteLine("✓ COM initialized for TSF (single-threaded apartment)");
                        Console.ResetColor();

                        var decisionMaker = TypeRight.Engine.EngineComposition.CreateDecisionMaker();
                        Console.ForegroundColor = ConsoleColor.Green;
                        Console.WriteLine("✓ Correction engine loaded and configured");
                        Console.ResetColor();
                    }
                    finally
                    {
                        CoUninitialize();
                    }
                }
                else
                {
                    Console.ForegroundColor = ConsoleColor.Red;
                    Console.WriteLine("✗ Both apartment models failed.");
                    Console.ResetColor();
                }
            }
        }
    }
}