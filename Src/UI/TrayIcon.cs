using System.Drawing;
using System.Runtime.InteropServices;

namespace MochiBot.Src.UI
{
    /// <summary>
    /// 系统托盘图标（Win32 Shell_NotifyIcon，无 WinForms 依赖）
    /// </summary>
    internal sealed class TrayIcon : IDisposable
    {
        private const int WM_TRAYICON = 0x0400 + 1; // WM_USER + 1
        private const int WM_LBUTTONUP = 0x0202;
        private const int NIM_ADD = 0x00000000;
        private const int NIM_MODIFY = 0x00000001;
        private const int NIM_DELETE = 0x00000002;
        private const int NIF_MESSAGE = 0x00000001;
        private const int NIF_ICON = 0x00000002;
        private const int NIF_TIP = 0x00000004;

        [DllImport("shell32.dll", CharSet = CharSet.Unicode)]
        private static extern bool Shell_NotifyIcon(int dwMessage, ref NOTIFYDATA pnid);

        [DllImport("user32.dll")]
        private static extern IntPtr LoadIcon(IntPtr hInstance, IntPtr lpIconName);

        [DllImport("user32.dll")]
        private static extern IntPtr CreateIconFromEx(byte[] pbIconBits, int dwIconSize);

        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
        private struct NOTIFYDATA
        {
            public int cbSize;
            public IntPtr hWnd;
            public int uID;
            public int uFlags;
            public int uCallbackMessage;
            public IntPtr hIcon;
            [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 128)]
            public string szTip;
        }

        private NOTIFYDATA _data;
        private IntPtr _hwnd;
        private bool _added;
        private readonly Action _onDoubleClick;

        /// <param name="hwnd">接收回调消息的窗口句柄</param>
        /// <param name="tip">鼠标悬停提示文字</param>
        /// <param name="onDoubleClick">双击托盘图标时的回调</param>
        public TrayIcon(IntPtr hwnd, string tip, Action onDoubleClick)
        {
            _hwnd = hwnd;
            _onDoubleClick = onDoubleClick;

            _data = new NOTIFYDATA
            {
                cbSize = Marshal.SizeOf<NOTIFYDATA>(),
                hWnd = hwnd,
                uID = 1,
                uFlags = NIF_MESSAGE | NIF_ICON | NIF_TIP,
                uCallbackMessage = WM_TRAYICON,
                hIcon = LoadIcon(IntPtr.Zero, (IntPtr)32512), // IDI_APPLICATION
                szTip = tip
            };
        }

        public void Show()
        {
            if (!_added)
            {
                Shell_NotifyIcon(NIM_ADD, ref _data);
                _added = true;
            }
        }

        public void Hide()
        {
            if (_added)
            {
                Shell_NotifyIcon(NIM_DELETE, ref _data);
                _added = false;
            }
        }

        /// <summary>处理托盘回调消息，返回 true 表示已处理</summary>
        public bool ProcessMessage(int msg, IntPtr wParam, IntPtr lParam)
        {
            if (msg == WM_TRAYICON && (int)lParam == WM_LBUTTONUP)
            {
                _onDoubleClick?.Invoke();
                return true;
            }
            return false;
        }

        public void Dispose()
        {
            Hide();
        }
    }
}
