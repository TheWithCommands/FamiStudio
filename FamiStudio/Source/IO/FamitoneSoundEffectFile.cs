﻿using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace FamiStudio
{
    class FamitoneSoundEffectFile
    {
        readonly byte[] RegisterMap = new byte[] { 0x80, 0, 0x81, 0x82, 0x83, 0, 0x84, 0x85, 0x86, 0, 0x87, 0x88, 0x89, 0, 0x8a };

        // TODO: Move somewhere that is shared between Music + SFX.
        private string db = ".byte";
        private string dw = ".word";
        private string ll = "@";

        private void SetupFormat(int format)
        {
            switch (format)
            {
                case AssemblyFormat.NESASM:
                    db = ".db";
                    dw = ".dw";
                    ll = ".";
                    break;
                case AssemblyFormat.CA65:
                    db = ".byte";
                    dw = ".word";
                    ll = "@";
                    break;
                case AssemblyFormat.ASM6:
                    db = "db";
                    dw = "dw";
                    ll = "@";
                    break;
            }
        }

        private RegisterWrite[] GetRegisterWrites(Song song, bool pal)
        {
            var regPlayer = new RegisterPlayer(pal, song.Project.OutputsStereoAudio);

            // HACK: Need to disable smooth vibrato since sweep registers are not supported.
            // TODO: Make smooth vibrato a bool in the channel state.
            var oldSmoothVibrato = Settings.SquareSmoothVibrato;
            Settings.SquareSmoothVibrato = false;

            var writes = regPlayer.GetRegisterValues(song);

            Settings.SquareSmoothVibrato = oldSmoothVibrato;

            return writes;
        }

        public bool Save(Project project, int[] songIds, int format, int machine, int kernel, string filename, string includeFilename)
        {
            if (!project.EnsureSongAssemblyNamesAreUnique())
            {
                return false;
            }

            SetupFormat(format);

            var modeStrings = new List<string>();
            if (machine == MachineType.NTSC || machine == MachineType.Dual) modeStrings.Add("ntsc");
            if (machine == MachineType.PAL  || machine == MachineType.Dual) modeStrings.Add("pal");

            var lines = new List<string>();

            lines.Add($"; This file is for the {(kernel == FamiToneKernel.FamiTone2 ? "FamiTone2 library" : "FamiStudio Sound Engine")} and was generated by FamiStudio\n");

            if (format == AssemblyFormat.CA65)
            {
                // For CA65 we add a re-export of the symbol prefixed with _ for the C code to see
                // For some reason though, we can only rexport these symbols either before they are defined,
                // or after all of the data is completely written.
                lines.Add("");
                lines.Add(".if FAMISTUDIO_CFG_C_BINDINGS");
                lines.Add(".export _sounds=sounds");
                lines.Add(".endif");
                lines.Add("");
            }

            lines.Add($"sounds:");

            lines.Add($"\t{dw} {ll}{modeStrings[0]}");
            lines.Add($"\t{dw} {ll}{modeStrings[1 % modeStrings.Count]}");

            foreach (var str in modeStrings)
            {
                lines.Add($"{ll}{str}:");
                foreach (var songId in songIds)
                {
                    var song = project.GetSong(songId);
                    lines.Add($"\t{dw} {ll}sfx_{str}_{Utils.MakeNiceAsmName(song.Name)}");
                }
                lines.Add("");
            }

            foreach (var str in modeStrings)
            {
                foreach (var songId in songIds)
                { 
                    var song = project.GetSong(songId);
                    var writes = GetRegisterWrites(song, str == "pal");

                    var lastChangeFrame = 0;
                    var lastZeroVolumeIdx = -1;
                    var volumeAllZero = true;
                    var volume = new int[4];
                    var regs = new int[32];
                    var effect = new List<byte>();
                    var lastByteIsOperand = false;

                    for (int i = 0; i < regs.Length; i++)
                        regs[i] = -1;

                    regs[0x00] = 0x30;
                    regs[0x04] = 0x30;
                    regs[0x08] = 0x80;
                    regs[0x0c] = 0x30;

                    foreach (var reg in writes)
                    {
                        if (reg.Register == NesApu.APU_PL1_VOL    ||
                            reg.Register == NesApu.APU_PL1_LO     ||
                            reg.Register == NesApu.APU_PL1_HI     ||
                            reg.Register == NesApu.APU_PL2_VOL    ||
                            reg.Register == NesApu.APU_PL2_LO     ||
                            reg.Register == NesApu.APU_PL2_HI     ||
                            reg.Register == NesApu.APU_TRI_LINEAR ||
                            reg.Register == NesApu.APU_TRI_LO     ||
                            reg.Register == NesApu.APU_TRI_HI     ||
                            reg.Register == NesApu.APU_NOISE_VOL  ||
                            reg.Register == NesApu.APU_NOISE_LO)
                        {
                            if (regs[reg.Register - 0x4000] != reg.Value)
                            {
                                if (reg.FrameNumber != lastChangeFrame)
                                {
                                    int numEmptyFrames = reg.FrameNumber - lastChangeFrame;

                                    while (numEmptyFrames >= 0)
                                    {
                                        effect.Add((byte)(Math.Min(numEmptyFrames, 127)));
                                        numEmptyFrames -= 127;
                                    }
                                }

                                switch (reg.Register)
                                {
                                    case 0x4000: volume[0] = reg.Value & 0x0f; break;
                                    case 0x4004: volume[1] = reg.Value & 0x0f; break;
                                    case 0x4008: volume[2] = reg.Value & 0x7f; break;
                                    case 0x400c: volume[3] = reg.Value & 0x0f; break;
                                }

                                if (!volumeAllZero)
                                {
                                    if (volume[0] == 0 && volume[1] == 0 && volume[2] == 0 && volume[3] == 0)
                                    {
                                        volumeAllZero = true;
                                        lastZeroVolumeIdx = effect.Count();
                                    }
                                }
                                else
                                {
                                    if (volume[0] != 0 || volume[1] != 0 || volume[2] != 0 || volume[3] != 0)
                                    {
                                        volumeAllZero = false;
                                    }
                                }

                                effect.Add(RegisterMap[reg.Register - 0x4000]);
                                effect.Add((byte)reg.Value);

                                if (effect.Count == 255)
                                    lastByteIsOperand = true;

                                regs[reg.Register - 0x4000] = reg.Value;

                                lastChangeFrame = reg.FrameNumber;
                            }
                        }
                    }

                    if (!volumeAllZero)
                    {
                        int numEmptyFrames = writes[writes.Length - 1].FrameNumber - lastChangeFrame;

                        while (numEmptyFrames > 0)
                        {
                            effect.Add((byte)(Math.Min(numEmptyFrames, 127)));
                            numEmptyFrames -= 127;
                        }
                    }
                    else if (lastZeroVolumeIdx >= 0)
                    {
                        effect.RemoveRange(lastZeroVolumeIdx, effect.Count - lastZeroVolumeIdx);
                    }

                    if (kernel == FamiToneKernel.FamiTone2 && effect.Count > 255)
                    {
                        Log.LogMessage(LogSeverity.Warning, $"Effect ({song.Name}) was longer than 256 bytes ({effect.Count}) and was truncated.");
                        var removeStart = lastByteIsOperand ? 254 : 255;
                        effect.RemoveRange(removeStart, effect.Count - removeStart);
                    }

                    effect.Add(0);

                    lines.Add($"{ll}sfx_{str}_{Utils.MakeNiceAsmName(song.Name)}:");

                    for (int i = 0; i < (effect.Count + 15) / 16; i++)
                        lines.Add($"\t{db} {string.Join(",", effect.Skip(i * 16).Take(Math.Min(16, effect.Count - i * 16)).Select(x => $"${x:x2}"))}");

                    Log.LogMessage(LogSeverity.Info, $"Effect ({song.Name}): {effect.Count} bytes.");
                }
            }

            if (format == AssemblyFormat.CA65)
            {
                lines.Add("");
                lines.Add(".export sounds");
            }

            File.WriteAllLines(filename, lines.ToArray());

            if (includeFilename != null)
            {
                var includeLines = new List<string>();

                for (int songIdx = 0; songIdx < songIds.Length; songIdx++)
                {
                    var songId = songIds[songIdx];
                    var song = project.GetSong(songId);
                    includeLines.Add($"sfx_{Utils.MakeNiceAsmName(song.Name)} = {songIdx}");
                }
                includeLines.Add($"sfx_max = {songIds.Length}");

                // For CA65, also include song names.
                if (format == AssemblyFormat.CA65)
                {
                    includeLines.Add("");
                    includeLines.Add(".if SFX_STRINGS");
                    includeLines.Add("sfx_strings:");

                    foreach (var songId in songIds)
                    {
                        var song = project.GetSong(songId);
                        includeLines.Add($".asciiz \"{song.Name}\"");
                    }

                    includeLines.Add(".endif");
                }

                File.WriteAllLines(includeFilename, includeLines.ToArray());
            }

            return true;
        }
    }
}
