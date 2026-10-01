/*
  This source is subject to the Microsoft Public License. See LICENSE.TXT for details.
  The original developer is Iros <irosff@outlook.com>
*/

using Iros;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;

namespace AppWrapper {
    static class HexPatch {

        [DllImport("kernel32.dll", SetLastError = true)]
        static extern bool VirtualProtect(IntPtr lpAddress, uint dwSize, Protection flNewProtect, out Protection lpflOldProtect);

        [DllImport("kernel32.dll", SetLastError = true)]
        static extern IntPtr GetCurrentProcess();

        [DllImport("kernel32.dll", SetLastError = true)]
        static extern bool FlushInstructionCache(IntPtr hProcess, IntPtr lpBaseAddress, uint dwSize);

        public enum Protection : int {
            PAGE_NOACCESS = 0x01,
            PAGE_READONLY = 0x02,
            PAGE_READWRITE = 0x04,
            PAGE_WRITECOPY = 0x08,
            PAGE_EXECUTE = 0x10,
            PAGE_EXECUTE_READ = 0x20,
            PAGE_EXECUTE_READWRITE = 0x40,
            PAGE_EXECUTE_WRITECOPY = 0x80,
            PAGE_GUARD = 0x100,
            PAGE_NOCACHE = 0x200,
            PAGE_WRITECOMBINE = 0x400
        }

        private static IEnumerable<string> FilterToUseful(IEnumerable<string> input) {
            bool inMLComment = false;
            foreach (string line in input.Skip(1)) {
                string t = line.Trim();
                if (String.IsNullOrEmpty(t)) continue;
                if (inMLComment) {
                    if (t.EndsWith("}}")) inMLComment = false;
                    continue;
                }
                if (t.StartsWith("{{")) {
                    inMLComment = true;
                    continue;
                }
                if (t.StartsWith("#")) continue;
                if (t.StartsWith("{")) continue;
                yield return t;
            }
        }

        private static byte[] GetBytes(string spec) {
            if (spec.Contains(':')) {
                string[] parts = spec.Split(':');
                byte b = byte.Parse(parts[0], System.Globalization.NumberStyles.HexNumber);
                int count = int.Parse(parts[1], System.Globalization.NumberStyles.HexNumber);
                return Enumerable.Repeat(b, count).ToArray();
            } else {
                return spec.Trim().Split(' ', ',', '\t').Select(s => byte.Parse(s, System.Globalization.NumberStyles.HexNumber)).ToArray();
            }
        }

        private static IntPtr GetAddress(string spec, int offset) {
            int abase;
            string[] sparts =spec.Split('+', '-').ToArray();
            if (sparts[0].EndsWith("^")) {
                IntPtr ptr = new IntPtr(int.Parse(sparts[0].Substring(0, sparts[0].Length - 1), System.Globalization.NumberStyles.HexNumber) + offset);
                sparts[0] = System.Runtime.InteropServices.Marshal.ReadInt32(ptr).ToString("x");
            } 
            int[] parts = sparts.Select(s => int.Parse(s, System.Globalization.NumberStyles.HexNumber)).ToArray();

            if (spec.Contains('+'))
                abase = parts[0] + parts[1];
            else if (spec.Contains('-'))
                abase = parts[0] - parts[1];
            else
                abase = parts[0];

            return new IntPtr(abase + offset);
        }

        private static void Apply(IEnumerable<string> lines) {
            int offset = 0;
            foreach (string instruct in FilterToUseful(lines)) {
                //DebugLogger.DetailedWriteLine("Processing HEXT instruction {instruct}");
                try
                {
                    if (instruct.StartsWith("+"))
                        offset = int.Parse(instruct.Substring(1), System.Globalization.NumberStyles.HexNumber);
                    else if (instruct.StartsWith("-"))
                        offset = -int.Parse(instruct.Substring(1), System.Globalization.NumberStyles.HexNumber);
                    else if (instruct.Contains('=') && (instruct.IndexOf("Delay", StringComparison.InvariantCultureIgnoreCase) < 0))
                    {
                        string[] parts = instruct.Split('=');
                        IntPtr addr = GetAddress(parts[0], offset);
                        byte[] bytes = GetBytes(parts[1]);
                        Protection originalProtection;
                        if (VirtualProtect(addr, (uint)bytes.Length, Protection.PAGE_EXECUTE_READWRITE, out originalProtection))
                        {
                            Util.CopyToIntPtr(bytes, addr, bytes.Length);
                            if (!FlushInstructionCache(GetCurrentProcess(), addr, (uint)bytes.Length))
                                DebugLogger.WriteLine($"FlushInstructionCache failed for Hext address {addr.ToInt64():X}");
                            // Hext code caves may live on pages that were originally data.
                            // Leave patched pages executable so injected calls can enter them.
                            if (((int)originalProtection & 0xF0) == 0)
                                DebugLogger.WriteLine($"Hext enabled execution at {addr.ToInt64():X} (previous protection: {originalProtection})");
                        }
                        else
                            DebugLogger.WriteLine($"Could not make Hext address {addr.ToInt64():X} writable");
                    }
                    else if (instruct.Contains(':'))
                    {
                        string[] parts = instruct.Split(':');
                        IntPtr addr = GetAddress(parts[0], offset);
                        int length = int.Parse(parts[1], System.Globalization.NumberStyles.HexNumber);
                        Protection _;
                        VirtualProtect(addr, (uint)length, Protection.PAGE_EXECUTE_READWRITE, out _);
                    }
                } catch (Exception) {
                    throw new System.Exception(instruct);
                }
            }
        }

        public static void Apply(System.IO.Stream source) {
            List<string> lines = new List<string>();
            using (var sr = new System.IO.StreamReader(source)) {
                string line;
                while ((line = sr.ReadLine()) != null) lines.Add(line);                
            }

            string delay = lines.FirstOrDefault(s => s.Trim().StartsWith("Delay", StringComparison.InvariantCultureIgnoreCase));
            if (delay != null) {
                delay = delay.Trim().Substring(5).Trim();
                if (delay.StartsWith("=")) delay = delay.Substring(1).Trim();
                int time;
                if (int.TryParse(delay, out time)) {
                    new System.Threading.Thread(() => {
                        System.Threading.Thread.Sleep(time);
                        DebugLogger.DetailedWriteLine("Applying delayed hex patch");
                        Apply(lines);
                    }) { IsBackground = true, Name = "ApplyDelayedHexPatch" }
                    .Start();
                }
            } else {
                Apply(lines);
            }
        }

    }
}
