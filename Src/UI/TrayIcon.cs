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
        private const int WM_RBUTTONUP = 0x0205;
        private const int WM_COMMAND = 0x0111;
        private const int NIM_ADD = 0x00000000;
        private const int NIM_MODIFY = 0x00000001;
        private const int NIM_DELETE = 0x00000002;
        private const int NIF_MESSAGE = 0x00000001;
        private const int NIF_ICON = 0x00000002;
        private const int NIF_TIP = 0x00000004;
        private const uint MF_STRING = 0x00000000;
        private const uint MF_CHECKED = 0x00000008;
        private const uint MF_SEPARATOR = 0x00000800;
        private const uint TPM_RIGHTBUTTON = 0x0002;
        private const uint TPM_BOTTOMALIGN = 0x0020;

        [DllImport("shell32.dll", CharSet = CharSet.Unicode)]
        private static extern bool Shell_NotifyIcon(int dwMessage, ref NOTIFYDATA pnid);

        [DllImport("user32.dll")]
        private static extern IntPtr LoadIcon(IntPtr hInstance, IntPtr lpIconName);

        [DllImport("user32.dll")]
        private static extern IntPtr CreatePopupMenu();

        [DllImport("user32.dll", CharSet = CharSet.Unicode)]
        private static extern bool AppendMenu(IntPtr hMenu, uint uFlags, uint uIDNewItem, string lpNewItem);

        [DllImport("user32.dll")]
        private static extern bool TrackPopupMenu(IntPtr hMenu, uint uFlags, int x, int y, int nReserved, IntPtr hWnd, IntPtr prcRect);

        [DllImport("user32.dll")]
        private static extern bool DestroyMenu(IntPtr hMenu);

        [DllImport("user32.dll")]
        private static extern bool GetCursorPos(out POINT lpPoint);

        [StructLayout(LayoutKind.Sequential)]
        private struct POINT { public int X; public int Y; }

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

        /// <summary>托盘右键菜单项</summary>
        /// <param name="Label">显示文字</param>
        /// <param name="Callback">点击回调</param>
        /// <param name="IsChecked">当前是否勾选（每次弹出时查询）</param>
        public sealed record MenuItem(string Label, Action Callback, Func<bool>? IsChecked = null);

        private NOTIFYDATA _data;
        private IntPtr _hwnd;
        private bool _added;
        private readonly Action _onDoubleClick;
        private readonly MenuItem[] _menuItems;

        /// <param name="hwnd">接收回调消息的窗口句柄</param>
        /// <param name="tip">鼠标悬停提示文字</param>
        /// <param name="onDoubleClick">双击托盘图标时的回调</param>
        /// <param name="menuItems">右键菜单项</param>
        public TrayIcon(IntPtr hwnd, string tip, Action onDoubleClick, MenuItem[]? menuItems = null)
        {
            _hwnd = hwnd;
            _onDoubleClick = onDoubleClick;
            _menuItems = menuItems ?? Array.Empty<MenuItem>();

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
            if (msg == WM_TRAYICON && (int)lParam == WM_RBUTTONUP)
            {
                ShowContextMenu();
                return true;
            }
            if (msg == WM_COMMAND)
            {
                int id = (int)wParam & 0xFFFF;
                if (id >= 1 && id <= _menuItems.Length)
                {
                    _menuItems[id - 1].Callback();
                    return true;
                }
            }
            return false;
        }

        private void ShowContextMenu()
        {
            GetCursorPos(out var cursor);
            var hMenu = CreatePopupMenu();
            try
            {
                for (int i = 0; i < _menuItems.Length; i++)
                {
                    var item = _menuItems[i];
                    uint flags = MF_STRING;
                    if (item.IsChecked?.Invoke() == true)
                        flags |= MF_CHECKED;
                    AppendMenu(hMenu, flags, (uint)(i + 1), item.Label);
                }
                TrackPopupMenu(hMenu, TPM_RIGHTBUTTON | TPM_BOTTOMALIGN,
                    cursor.X, cursor.Y, 0, _hwnd, IntPtr.Zero);
            }
            finally
            {
                DestroyMenu(hMenu);
            }
        }

        public void Dispose()
        {
            Hide();
        }
    }
}
