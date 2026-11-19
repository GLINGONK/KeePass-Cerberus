using System;
using System.IO;
using System.Runtime.InteropServices;
using System.Security;
using System.Text;
using System.Windows;
using System.Windows.Threading;

using KeePassLib.Security;
using KeePassLib.Utility;

namespace Cerberus.App.Services
{
	internal static class Secure
	{
		/// <summary>UTF-8 bytes of a SecureString; caller must zero the result.</summary>
		public static byte[] Utf8(SecureString ss)
		{
			if(ss == null || ss.Length == 0) return new byte[0];
			IntPtr p = IntPtr.Zero;
			char[] v = new char[ss.Length];
			try
			{
				p = Marshal.SecureStringToGlobalAllocUnicode(ss);
				Marshal.Copy(p, v, 0, v.Length);
				return Encoding.UTF8.GetBytes(v);
			}
			finally
			{
				Array.Clear(v, 0, v.Length);
				if(p != IntPtr.Zero) Marshal.ZeroFreeGlobalAllocUnicode(p);
			}
		}

		public static ProtectedString ToProtected(SecureString ss)
		{
			byte[] pb = Utf8(ss);
			try { return new ProtectedString(true, pb); }
			finally { MemUtil.ZeroByteArray(pb); }
		}

		public static char[] Chars(SecureString ss)
		{
			if(ss == null || ss.Length == 0) return new char[0];
			IntPtr p = IntPtr.Zero;
			char[] v = new char[ss.Length];
			try
			{
				p = Marshal.SecureStringToGlobalAllocUnicode(ss);
				Marshal.Copy(p, v, 0, v.Length);
				return v;
			}
			finally { if(p != IntPtr.Zero) Marshal.ZeroFreeGlobalAllocUnicode(p); }
		}
	}

	/// <summary>
	/// Clipboard with auto-clear and exclusion from Windows clipboard history /
	/// cloud clipboard / clipboard monitors (same formats as KeePass).
	/// </summary>
	internal static class SafeClipboard
	{
		private static DispatcherTimer g_timer;
		private static string g_strLast;

		public static event Action<int> Countdown;

		public static void Copy(string strText)
		{
			if(strText == null) strText = string.Empty;
			try
			{
				DataObject d = new DataObject();
				d.SetData(DataFormats.UnicodeText, strText);
				d.SetData("ExcludeClipboardContentFromMonitorProcessing", new MemoryStream(new byte[] { 1, 0, 0, 0 }));
				d.SetData("CanIncludeInClipboardHistory", new MemoryStream(new byte[] { 0, 0, 0, 0 }));
				d.SetData("CanUploadToCloudClipboard", new MemoryStream(new byte[] { 0, 0, 0, 0 }));
				Clipboard.SetDataObject(d, true);
				g_strLast = strText;
			}
			catch(Exception) { return; }

			int nSec = Settings.Current.ClipboardClearSeconds;
			if(nSec <= 0) return;
			if(g_timer != null) g_timer.Stop();
			int nLeft = nSec;
			g_timer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(1) };
			g_timer.Tick += delegate
			{
				--nLeft;
				Countdown?.Invoke(nLeft);
				if(nLeft <= 0) { g_timer.Stop(); ClearIfOurs(); }
			};
			g_timer.Start();
			Countdown?.Invoke(nLeft);
		}

		public static void ClearIfOurs()
		{
			try
			{
				if(g_strLast != null && Clipboard.ContainsText() && Clipboard.GetText() == g_strLast)
					Clipboard.Clear();
			}
			catch(Exception) { }
			g_strLast = null;
			Countdown?.Invoke(0);
		}
	}
}
