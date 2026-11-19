using System;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;

namespace Cerberus.App.Services
{
	internal static class Native
	{
		private const uint WDA_NONE = 0x0;
		private const uint WDA_MONITOR = 0x1; // black rectangle in captures (Windows 7+)

		[DllImport("user32.dll", SetLastError = true)]
		private static extern bool SetWindowDisplayAffinity(IntPtr hWnd, uint dwAffinity);

		[DllImport("dwmapi.dll")]
		private static extern int DwmSetWindowAttribute(IntPtr hwnd, int attr, ref int value, int size);

		[StructLayout(LayoutKind.Sequential)]
		private struct LASTINPUTINFO { public uint cbSize; public uint dwTime; }

		[DllImport("user32.dll")]
		private static extern bool GetLastInputInfo(ref LASTINPUTINFO plii);

		public static IntPtr Handle(Window w)
		{
			return new WindowInteropHelper(w).Handle;
		}

		public static void SetCaptureProtection(Window w, bool bProtect)
		{
			try
			{
				IntPtr h = Handle(w);
				if(h != IntPtr.Zero) SetWindowDisplayAffinity(h, bProtect ? WDA_MONITOR : WDA_NONE);
			}
			catch(Exception) { }
		}

		/// <summary>Dark title bar on Windows 10 1809+ / 11; ignored elsewhere.</summary>
		public static void SetDarkTitleBar(Window w, bool bDark)
		{
			try
			{
				IntPtr h = Handle(w);
				if(h == IntPtr.Zero) return;
				int v = (bDark ? 1 : 0);
				if(DwmSetWindowAttribute(h, 20, ref v, 4) != 0)
					DwmSetWindowAttribute(h, 19, ref v, 4);
			}
			catch(Exception) { }
		}

		/// <summary>Milliseconds since the last keyboard/mouse input (system wide).</summary>
		public static uint IdleMilliseconds()
		{
			LASTINPUTINFO lii = new LASTINPUTINFO();
			lii.cbSize = (uint)Marshal.SizeOf(typeof(LASTINPUTINFO));
			if(!GetLastInputInfo(ref lii)) return 0;
			return unchecked((uint)Environment.TickCount - lii.dwTime);
		}

		public static bool IsWindows10OrNewer
		{
			get
			{
				// Environment.OSVersion lies without manifest; the app manifest declares Windows 10.
				Version v = Environment.OSVersion.Version;
				return v.Major >= 10;
			}
		}
	}
}
