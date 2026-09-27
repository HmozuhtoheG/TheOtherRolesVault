using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using System.Threading;

#pragma warning disable CA1416

namespace TheOtherRoles.Modules.Emotes
{
    public static class EmoteFileDialog
    {
        private const int OFN_NOCHANGEDIR = 0x00000008;
        private const int OFN_ALLOWMULTISELECT = 0x00000200;
        private const int OFN_PATHMUSTEXIST = 0x00000800;
        private const int OFN_FILEMUSTEXIST = 0x00001000;
        private const int OFN_EXPLORER = 0x00080000;

        private const int FileBufferChars = 4096;

        private const string Filter = "Emote (*.png;*.gif)\0*.png;*.gif\0\0";
        private const string Title = "Select emote images";
        private const string DefaultExtension = "png";

        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
        private struct OpenFileName
        {
            public int lStructSize;
            public IntPtr hwndOwner;
            public IntPtr hInstance;
            public IntPtr lpstrFilter;
            public IntPtr lpstrCustomFilter;
            public int nMaxCustFilter;
            public int nFilterIndex;
            public IntPtr lpstrFile;
            public int nMaxFile;
            public IntPtr lpstrFileTitle;
            public int nMaxFileTitle;
            public IntPtr lpstrInitialDir;
            public IntPtr lpstrTitle;
            public int Flags;
            public short nFileOffset;
            public short nFileExtension;
            public IntPtr lpstrDefExt;
            public IntPtr lCustData;
            public IntPtr lpfnHook;
            public IntPtr lpTemplateName;
            public IntPtr pvReserved;
            public int dwReserved;
            public int flagsEx;
        }

        [DllImport("comdlg32.dll", CharSet = CharSet.Unicode, ExactSpelling = true, SetLastError = true)]
        private static extern bool GetOpenFileNameW(IntPtr ofn);

        [DllImport("comdlg32.dll", ExactSpelling = true)]
        private static extern int CommDlgExtendedError();

        [DllImport("user32.dll", ExactSpelling = true)]
        private static extern IntPtr GetForegroundWindow();

        private static volatile bool running;
        private static List<string> pending;

        public static bool IsBusy => running;

        public static void Open()
        {
            if (running) return;
#if WINDOWS
            running = true;
            var owner = GetForegroundWindow();
            var thread = new Thread(() => Run(owner))
            {
                IsBackground = true,
                Name = "TORVEmoteFileDialog"
            };
            thread.SetApartmentState(ApartmentState.STA);
            thread.Start();
#else
            TheOtherRolesPlugin.Logger.LogInfo("[EmoteFileDialog] not supported on this platform");
#endif
        }

        public static bool TryConsume(out List<string> files)
        {
            files = null;
            var result = Interlocked.Exchange(ref pending, null);
            if (result == null) return false;
            files = result;
            return true;
        }

#if WINDOWS
        private static void Run(IntPtr owner)
        {
            IntPtr filterPtr = IntPtr.Zero;
            IntPtr filePtr = IntPtr.Zero;
            IntPtr dirPtr = IntPtr.Zero;
            IntPtr titlePtr = IntPtr.Zero;
            IntPtr extPtr = IntPtr.Zero;
            IntPtr structurePtr = IntPtr.Zero;

            try
            {
                filterPtr = AllocChars(Filter);
                filePtr = Marshal.AllocHGlobal(FileBufferChars * sizeof(char));
                dirPtr = AllocChars(EmoteImport.Directory);
                titlePtr = AllocChars(Title);
                extPtr = AllocChars(DefaultExtension);

                Marshal.Copy(new char[FileBufferChars], 0, filePtr, FileBufferChars);

                var ofn = new OpenFileName
                {
                    lStructSize = Marshal.SizeOf<OpenFileName>(),
                    hwndOwner = owner,
                    lpstrFilter = filterPtr,
                    nFilterIndex = 1,
                    lpstrFile = filePtr,
                    nMaxFile = FileBufferChars,
                    lpstrInitialDir = dirPtr,
                    lpstrTitle = titlePtr,
                    lpstrDefExt = extPtr,
                    Flags = OFN_EXPLORER | OFN_FILEMUSTEXIST | OFN_PATHMUSTEXIST | OFN_ALLOWMULTISELECT | OFN_NOCHANGEDIR
                };

                structurePtr = Marshal.AllocHGlobal(ofn.lStructSize);
                Marshal.StructureToPtr(ofn, structurePtr, false);

                bool ok = GetOpenFileNameW(structurePtr);
                if (!ok)
                {
                    int error = CommDlgExtendedError();
                    if (error != 0) TheOtherRolesPlugin.Logger.LogWarning($"[EmoteFileDialog] cancelled with error 0x{error:X}");
                    return;
                }

                var chars = new char[FileBufferChars];
                Marshal.Copy(filePtr, chars, 0, FileBufferChars);
                var files = Parse(chars);
                TheOtherRolesPlugin.Logger.LogInfo($"[EmoteFileDialog] picked {files.Count} file(s)");
                pending = files;
            }
            catch (Exception ex)
            {
                TheOtherRolesPlugin.Logger.LogWarning($"[EmoteFileDialog] {ex}");
            }
            finally
            {
                if (structurePtr != IntPtr.Zero) Marshal.FreeHGlobal(structurePtr);
                if (filterPtr != IntPtr.Zero) Marshal.FreeHGlobal(filterPtr);
                if (filePtr != IntPtr.Zero) Marshal.FreeHGlobal(filePtr);
                if (dirPtr != IntPtr.Zero) Marshal.FreeHGlobal(dirPtr);
                if (titlePtr != IntPtr.Zero) Marshal.FreeHGlobal(titlePtr);
                if (extPtr != IntPtr.Zero) Marshal.FreeHGlobal(extPtr);
                running = false;
            }
        }

        private static IntPtr AllocChars(string value)
        {
            var chars = value.ToCharArray();
            var pointer = Marshal.AllocHGlobal((chars.Length + 1) * sizeof(char));
            Marshal.Copy(chars, 0, pointer, chars.Length);
            Marshal.WriteInt16(pointer, chars.Length * sizeof(char), 0);
            return pointer;
        }

        private static List<string> Parse(char[] chars)
        {
            var files = new List<string>();

            int firstEnd = Array.IndexOf(chars, '\0');
            if (firstEnd <= 0) return files;

            var first = new string(chars, 0, firstEnd);

            int index = firstEnd + 1;
            while (index < FileBufferChars && chars[index] != '\0')
            {
                int end = index;
                while (end < FileBufferChars && chars[end] != '\0') end++;
                files.Add(System.IO.Path.Combine(first, new string(chars, index, end - index)));
                index = end + 1;
            }

            if (files.Count == 0) files.Add(first);
            return files;
        }
#endif
    }
}
