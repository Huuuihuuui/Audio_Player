// 文件夹选择对话框 —— 使用 Windows Shell COM 接口打开原生文件夹选择窗口
using System.Diagnostics;
using System.Runtime.InteropServices;

namespace MusicPlayer.Helpers;

public static class FolderBrowser
{
    // 打开 Windows 原生文件夹选择对话框，返回用户选中的路径（取消则返回 null）
    public static string? ShowDialog(string description = "选择文件夹")
    {
        var dialog = new FolderBrowserDialog();
        return dialog.Show(description);
    }

    // 内部封装：调用 ole32 的 IFileDialog 实现
    private class FolderBrowserDialog
    {
        [ComImport, Guid("DC1C5A9C-E88A-4dde-A5A1-60F82A20AEF7")]
        private class FileOpenDialog { }

        [ComImport, Guid("42F85136-DB7E-439C-85F1-E4075D135FC8"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
        private interface IFileOpenDialog
        {
            [PreserveSig] int Show(IntPtr parent);
            [PreserveSig] int SetFileTypes(uint cFileTypes, IntPtr rgFilterSpec);  // COMDLG_FILTERSPEC*
            [PreserveSig] int SetFileTypeIndex(uint iFileType);
            [PreserveSig] int GetFileTypeIndex(out uint piFileType);
            [PreserveSig] int Advise(IntPtr pfde, out uint pdwCookie);
            [PreserveSig] int Unadvise(uint dwCookie);
            [PreserveSig] int SetOptions(uint fos);
            [PreserveSig] int GetOptions(out uint pfos);
            [PreserveSig] int SetDefaultFolder(IntPtr psi);
            [PreserveSig] int SetFolder(IntPtr psi);
            [PreserveSig] int GetFolder(out IntPtr ppsi);
            [PreserveSig] int GetCurrentSelection(out IntPtr ppsi);
            [PreserveSig] int SetFileName([MarshalAs(UnmanagedType.LPWStr)] string pszName);
            [PreserveSig] int GetFileName([MarshalAs(UnmanagedType.LPWStr)] out string pszName);
            [PreserveSig] int SetTitle([MarshalAs(UnmanagedType.LPWStr)] string pszTitle);
            [PreserveSig] int SetOkButtonLabel([MarshalAs(UnmanagedType.LPWStr)] string pszText);
            [PreserveSig] int SetFileNameLabel([MarshalAs(UnmanagedType.LPWStr)] string pszLabel);
            [PreserveSig] int GetResult(out IntPtr ppsi);
            [PreserveSig] int AddPlace(IntPtr psi, uint fdap);
            [PreserveSig] int SetDefaultExtension([MarshalAs(UnmanagedType.LPWStr)] string pszDefaultExtension);
            [PreserveSig] int Close(int hr);
            [PreserveSig] int SetClientGuid(ref Guid guid);
            [PreserveSig] int ClearClientData();
            [PreserveSig] int SetFilter(IntPtr pFilter);
            [PreserveSig] int GetResults(out IntPtr ppenum);
            [PreserveSig] int GetSelectedItems(out IntPtr ppsai);
        }

        [ComImport, Guid("43826D1E-E718-42EE-BC55-A1E261C37BFE"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
        private interface IShellItem
        {
            [PreserveSig] int BindToHandler(IntPtr pbc, ref Guid bhid, ref Guid riid, out IntPtr ppv);
            [PreserveSig] int GetParent(out IShellItem ppsi);
            [PreserveSig] int GetDisplayName(uint sigdnName, out IntPtr ppszName);
            [PreserveSig] int GetAttributes(uint sfgaoMask, out uint psfgaoAttribs);
            [PreserveSig] int Compare(IShellItem psi, uint hint, out int piOrder);
        }

        private const uint FOS_PICKFOLDERS = 0x00000020;
        private const uint SIGDN_FILESYSPATH = 0x80058000;

        public string? Show(string title)
        {
            var dialog = (IFileOpenDialog)new FileOpenDialog();

            try
            {
                dialog.SetTitle(title);
                dialog.SetOptions(FOS_PICKFOLDERS);

                var hr = dialog.Show(IntPtr.Zero);
                if (hr != 0) return null; // 用户取消

                dialog.GetResult(out var itemPtr);
                if (itemPtr == IntPtr.Zero) return null;

                var item = (IShellItem)Marshal.GetTypedObjectForIUnknown(itemPtr, typeof(IShellItem));
                item.GetDisplayName(SIGDN_FILESYSPATH, out var pathPtr);

                var path = Marshal.PtrToStringAuto(pathPtr);
                Marshal.FreeCoTaskMem(pathPtr);
                Marshal.ReleaseComObject(item);
                Marshal.Release(itemPtr);

                return path;
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"FolderBrowser error: {ex.Message}");
                return null;
            }
            finally
            {
                Marshal.ReleaseComObject(dialog);
            }
        }
    }
}
