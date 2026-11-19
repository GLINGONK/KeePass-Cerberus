using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;

using KeePassLib;
using KeePassLib.Collections;

namespace Cerberus.App.Services
{
	/// <summary>
	/// Auto-type compatible with KeePass sequences:
	///   {USERNAME} {PASSWORD} {TITLE} {URL} {NOTES} {S:Field} {TOTP} {TIMEOTP}
	///   {TAB} {ENTER} {SPACE} {BS} {BACKSPACE} {DEL} {DELETE} {ESC} {INSERT} {HOME} {END}
	///   {UP} {DOWN} {LEFT} {RIGHT} {PGUP} {PGDN} {F1}..{F16} {WIN}
	///   {KEY n} repeat (e.g. {TAB 3}), {DELAY n}, {DELAY=n} (default delay), {CLEARFIELD}
	///   + Shift, ^ Ctrl, % Alt (apply to the next key), {+} {^} {%} {~} {(} {)} {{} {}} literals, ~ = Enter
	/// </summary>
	internal static class AutoType
	{
		// ---------- sequence resolution ----------

		public static string SequenceFor(PwEntry pe, string strWindowTitle)
		{
			// Window association with its own sequence wins.
			if(strWindowTitle != null)
			{
				foreach(AutoTypeAssociation a in pe.AutoType.Associations)
				{
					if(WindowMatches(a.WindowName, strWindowTitle) && !string.IsNullOrEmpty(a.Sequence))
						return a.Sequence;
				}
			}
			if(!string.IsNullOrEmpty(pe.AutoType.DefaultSequence)) return pe.AutoType.DefaultSequence;
			if(pe.ParentGroup != null)
			{
				string s = pe.ParentGroup.GetAutoTypeSequenceInherited();
				if(!string.IsNullOrEmpty(s)) return s;
			}
			return PwDefs.DefaultAutoTypeSequence;
		}

		public static bool IsEnabled(PwEntry pe)
		{
			if(!pe.AutoType.Enabled) return false;
			return pe.ParentGroup == null || pe.ParentGroup.GetAutoTypeEnabledInherited();
		}

		/// <summary>KeePass wildcard (*) or //regex// window pattern.</summary>
		public static bool WindowMatches(string strPattern, string strTitle)
		{
			if(string.IsNullOrEmpty(strPattern) || strTitle == null) return false;
			strPattern = strPattern.Trim();
			try
			{
				if(strPattern.Length > 4 && strPattern.StartsWith("//") && strPattern.EndsWith("//"))
					return Regex.IsMatch(strTitle, strPattern.Substring(2, strPattern.Length - 4), RegexOptions.IgnoreCase);
				string rx = "^" + Regex.Escape(strPattern).Replace("\\*", ".*").Replace("\\?", ".") + "$";
				return Regex.IsMatch(strTitle, rx, RegexOptions.IgnoreCase);
			}
			catch(Exception) { return false; }
		}

		/// <summary>Entries matching the active window (associations, or entry title contained in the window title).</summary>
		/// <summary>
		/// Entries for the active window. bExplicit = true when they come from
		/// user-defined window associations (safe to type directly); otherwise
		/// they are guesses from the title / URL, which a malicious web page can
		/// spoof, so the caller must ask the user to confirm.
		/// </summary>
		public static List<PwEntry> FindForWindow(string strTitle, out bool bExplicit)
		{
			bExplicit = false;
			List<PwEntry> lExplicit = new List<PwEntry>();
			if(Session.IsOpen && !string.IsNullOrEmpty(strTitle))
			{
				foreach(PwEntry pe in Session.AllEntries(false))
					if(IsEnabled(pe) && pe.AutoType.Associations.Any(a => WindowMatches(a.WindowName, strTitle))) lExplicit.Add(pe);
			}
			if(lExplicit.Count > 0) { bExplicit = true; return lExplicit; }
			return FindHeuristic(strTitle);
		}

		private static List<PwEntry> FindHeuristic(string strTitle)
		{
			List<PwEntry> l = new List<PwEntry>();
			if(!Session.IsOpen || string.IsNullOrEmpty(strTitle)) return l;
			foreach(PwEntry pe in Session.AllEntries(false))
			{
				if(!IsEnabled(pe)) continue;
				bool m = false;
				if(!m)
				{
					string t = pe.Strings.ReadSafe(PwDefs.TitleField).Trim();
					if(t.Length >= 2 && strTitle.IndexOf(t, StringComparison.CurrentCultureIgnoreCase) >= 0) m = true;
				}
				if(!m)
				{
					string host = Host(pe.Strings.ReadSafe(PwDefs.UrlField));
					if(host.Length > 0 && strTitle.IndexOf(host, StringComparison.OrdinalIgnoreCase) >= 0) m = true;
				}
				if(m) l.Add(pe);
			}
			return l;
		}

		private static string Host(string url)
		{
			try
			{
				if(url.Trim().Length == 0) return "";
				string h = new Uri(url.Contains("://") ? url : "https://" + url).Host;
				return h.StartsWith("www.") ? h.Substring(4) : h;
			}
			catch(Exception) { return ""; }
		}

		// ---------- placeholder expansion ----------

		private static string Field(PwEntry pe, string name)
		{
			switch(name.ToUpperInvariant())
			{
				case "USERNAME": return pe.Strings.ReadSafe(PwDefs.UserNameField);
				case "PASSWORD": return pe.Strings.ReadSafe(PwDefs.PasswordField);
				case "TITLE": return pe.Strings.ReadSafe(PwDefs.TitleField);
				case "URL": return pe.Strings.ReadSafe(PwDefs.UrlField);
				case "NOTES": return pe.Strings.ReadSafe(PwDefs.NotesField);
				case "TOTP":
				case "TIMEOTP":
					Totp.Params p = Totp.FromEntry(pe);
					if(p == null) return "";
					int left; return Totp.Code(p, DateTime.UtcNow, out left);
			}
			return null;
		}

		// ---------- tokens ----------

		private enum TokKind { Text, Key, Delay, DefaultDelay, ClearField }

		private sealed class Tok
		{
			public TokKind Kind;
			public string Text;
			public ushort Vk;
			public int Count = 1;
			public bool Shift, Ctrl, Alt;
		}

		private static readonly Dictionary<string, ushort> Keys = new Dictionary<string, ushort>(StringComparer.OrdinalIgnoreCase)
		{
			{ "TAB", 0x09 }, { "ENTER", 0x0D }, { "SPACE", 0x20 }, { "BS", 0x08 }, { "BKSP", 0x08 }, { "BACKSPACE", 0x08 },
			{ "DEL", 0x2E }, { "DELETE", 0x2E }, { "ESC", 0x1B }, { "INSERT", 0x2D }, { "INS", 0x2D }, { "HOME", 0x24 }, { "END", 0x23 },
			{ "UP", 0x26 }, { "DOWN", 0x28 }, { "LEFT", 0x25 }, { "RIGHT", 0x27 }, { "PGUP", 0x21 }, { "PGDN", 0x22 },
			{ "WIN", 0x5B }, { "LWIN", 0x5B }, { "RWIN", 0x5C }, { "APPS", 0x5D }, { "CAPSLOCK", 0x14 }, { "NUMLOCK", 0x90 },
			{ "SCROLLLOCK", 0x91 }, { "PRTSC", 0x2C }, { "BREAK", 0x13 }, { "HELP", 0x2F }
		};

		private static List<Tok> Parse(string seq, PwEntry pe)
		{
			List<Tok> l = new List<Tok>();
			bool sh = false, ct = false, al = false;
			Action<Tok> add = t => { t.Shift = sh; t.Ctrl = ct; t.Alt = al; sh = ct = al = false; l.Add(t); };
			int i = 0;
			while(i < seq.Length)
			{
				char c = seq[i];
				if(c == '+') { sh = true; ++i; continue; }
				if(c == '^') { ct = true; ++i; continue; }
				if(c == '%') { al = true; ++i; continue; }
				if(c == '~') { add(new Tok { Kind = TokKind.Key, Vk = 0x0D }); ++i; continue; }
				if(c != '{') { add(new Tok { Kind = TokKind.Text, Text = c.ToString() }); ++i; continue; }

				int j = seq.IndexOf('}', i + 2 <= seq.Length ? i + 2 : i + 1);
				if(seq.Length > i + 2 && seq[i + 1] == '}' && seq[i + 2] == '}') j = i + 2; // {}}
				if(j < 0) { add(new Tok { Kind = TokKind.Text, Text = seq.Substring(i) }); break; }
				string inner = seq.Substring(i + 1, j - i - 1);
				i = j + 1;

				if(inner.Length == 1 && "+^%~(){}[]".IndexOf(inner[0]) >= 0) { add(new Tok { Kind = TokKind.Text, Text = inner }); continue; }
				if(inner.StartsWith("S:", StringComparison.OrdinalIgnoreCase))
				{
					add(new Tok { Kind = TokKind.Text, Text = pe.Strings.ReadSafe(inner.Substring(2)) });
					continue;
				}
				string f = Field(pe, inner);
				if(f != null) { add(new Tok { Kind = TokKind.Text, Text = f }); continue; }
				if(inner.Equals("CLEARFIELD", StringComparison.OrdinalIgnoreCase)) { add(new Tok { Kind = TokKind.ClearField }); continue; }

				Match md = Regex.Match(inner, @"^DELAY\s*=\s*(\d+)$", RegexOptions.IgnoreCase);
				if(md.Success) { l.Add(new Tok { Kind = TokKind.DefaultDelay, Count = int.Parse(md.Groups[1].Value) }); continue; }
				md = Regex.Match(inner, @"^DELAY\s+(\d+)$", RegexOptions.IgnoreCase);
				if(md.Success) { l.Add(new Tok { Kind = TokKind.Delay, Count = int.Parse(md.Groups[1].Value) }); continue; }

				Match mk = Regex.Match(inner, @"^([A-Za-z0-9]+)(?:\s+(\d+))?$");
				if(mk.Success)
				{
					string k = mk.Groups[1].Value;
					int n = mk.Groups[2].Success ? Math.Min(200, int.Parse(mk.Groups[2].Value)) : 1;
					ushort vk;
					bool ok = Keys.TryGetValue(k, out vk);
					Match mf = Regex.Match(k, @"^F(\d{1,2})$", RegexOptions.IgnoreCase);
					if(!ok && mf.Success)
					{
						int fn = int.Parse(mf.Groups[1].Value);
						if(fn >= 1 && fn <= 24) { vk = (ushort)(0x70 + fn - 1); ok = true; }
					}
					if(ok) { add(new Tok { Kind = TokKind.Key, Vk = vk, Count = n }); continue; }
				}
				// unknown placeholder: type it literally (like KeePass would leave it)
				add(new Tok { Kind = TokKind.Text, Text = "{" + inner + "}" });
			}
			return l;
		}

		// ---------- sending ----------

		[StructLayout(LayoutKind.Sequential)]
		private struct INPUT { public uint type; public InputUnion u; }
		[StructLayout(LayoutKind.Explicit)]
		private struct InputUnion { [FieldOffset(0)] public KEYBDINPUT ki; [FieldOffset(0)] public MOUSEINPUT mi; }
		[StructLayout(LayoutKind.Sequential)]
		private struct KEYBDINPUT { public ushort wVk; public ushort wScan; public uint dwFlags; public uint time; public IntPtr dwExtraInfo; }
		[StructLayout(LayoutKind.Sequential)]
		private struct MOUSEINPUT { public int dx, dy; public uint mouseData, dwFlags, time; public IntPtr dwExtraInfo; }

		private const uint INPUT_KEYBOARD = 1, KEYEVENTF_KEYUP = 0x2, KEYEVENTF_UNICODE = 0x4, KEYEVENTF_EXTENDEDKEY = 0x1;
		private const ushort VK_SHIFT = 0x10, VK_CONTROL = 0x11, VK_MENU = 0x12;

		[DllImport("user32.dll", SetLastError = true)] private static extern uint SendInput(uint n, INPUT[] inputs, int cb);
		[DllImport("user32.dll")] private static extern short GetAsyncKeyState(int vk);
		[DllImport("user32.dll")] public static extern IntPtr GetForegroundWindow();
		[DllImport("user32.dll")] public static extern bool SetForegroundWindow(IntPtr h);
		[DllImport("user32.dll", CharSet = CharSet.Unicode)] private static extern int GetWindowText(IntPtr h, StringBuilder sb, int n);
		[DllImport("user32.dll")] private static extern uint GetWindowThreadProcessId(IntPtr h, out uint pid);

		public static string WindowTitle(IntPtr h)
		{
			StringBuilder sb = new StringBuilder(512);
			GetWindowText(h, sb, sb.Capacity);
			return sb.ToString();
		}

		public static bool IsOwnWindow(IntPtr h)
		{
			uint pid; GetWindowThreadProcessId(h, out pid);
			return pid == (uint)System.Diagnostics.Process.GetCurrentProcess().Id;
		}

		private static INPUT Key(ushort vk, bool up)
		{
			bool ext = (vk >= 0x21 && vk <= 0x2E) || vk == 0x5B || vk == 0x5C || vk == 0x5D;
			return new INPUT { type = INPUT_KEYBOARD, u = new InputUnion { ki = new KEYBDINPUT { wVk = vk,
				dwFlags = (up ? KEYEVENTF_KEYUP : 0) | (ext ? KEYEVENTF_EXTENDEDKEY : 0) } } };
		}

		private static INPUT Uni(char ch, bool up)
		{
			return new INPUT { type = INPUT_KEYBOARD, u = new InputUnion { ki = new KEYBDINPUT { wScan = ch,
				dwFlags = KEYEVENTF_UNICODE | (up ? KEYEVENTF_KEYUP : 0) } } };
		}

		private static void Send(params INPUT[] v)
		{
			SendInput((uint)v.Length, v, Marshal.SizeOf(typeof(INPUT)));
		}

		private static void Mods(Tok t, bool up)
		{
			if(t.Shift) Send(Key(VK_SHIFT, up));
			if(t.Ctrl) Send(Key(VK_CONTROL, up));
			if(t.Alt) Send(Key(VK_MENU, up));
		}

		/// <summary>Wait until the user released the hotkey modifiers (Ctrl/Alt/Shift/Win).</summary>
		private static void WaitModifiersReleased()
		{
			for(int i = 0; i < 100; ++i)
			{
				bool down = (GetAsyncKeyState(VK_SHIFT) & 0x8000) != 0 || (GetAsyncKeyState(VK_CONTROL) & 0x8000) != 0 ||
					(GetAsyncKeyState(VK_MENU) & 0x8000) != 0 || (GetAsyncKeyState(0x5B) & 0x8000) != 0 || (GetAsyncKeyState(0x5C) & 0x8000) != 0;
				if(!down) return;
				Thread.Sleep(20);
			}
		}

		/// <summary>Type the sequence into the given window (call from a worker thread).</summary>
		public static void Perform(PwEntry pe, string seq, IntPtr hTarget)
		{
			List<Tok> toks = Parse(seq, pe);
			WaitModifiersReleased();
			if(hTarget != IntPtr.Zero) { SetForegroundWindow(hTarget); Thread.Sleep(150); }

			int delay = 10;
			try
			{
				foreach(Tok t in toks)
				{
					// abort if the user switched to another window meanwhile
					if(hTarget != IntPtr.Zero && GetForegroundWindow() != hTarget) return;
					switch(t.Kind)
					{
						case TokKind.DefaultDelay: delay = Math.Min(t.Count, 5000); break;
						case TokKind.Delay: Thread.Sleep(Math.Min(t.Count, 60000)); break;
						case TokKind.ClearField:
							Send(Key(VK_CONTROL, false), Key(0x41, false), Key(0x41, true), Key(VK_CONTROL, true));
							Thread.Sleep(delay);
							Send(Key(0x2E, false), Key(0x2E, true));
							Thread.Sleep(delay);
							break;
						case TokKind.Key:
							for(int n = 0; n < t.Count; ++n)
							{
								Mods(t, false);
								Send(Key(t.Vk, false), Key(t.Vk, true));
								Mods(t, true);
								Thread.Sleep(delay);
							}
							break;
						case TokKind.Text:
							bool hasMods = t.Shift || t.Ctrl || t.Alt;
							foreach(char ch in t.Text ?? "")
							{
								if(hasMods && ch < 128 && char.IsLetterOrDigit(ch))
								{
									// e.g. ^a = Ctrl+A : use virtual keys for shortcuts
									Mods(t, false);
									ushort vk = (ushort)char.ToUpperInvariant(ch);
									Send(Key(vk, false), Key(vk, true));
									Mods(t, true);
								}
								else if(ch == '\n') Send(Key(0x0D, false), Key(0x0D, true));
								else if(ch == '\r') continue;
								else Send(Uni(ch, false), Uni(ch, true));
								Thread.Sleep(delay);
							}
							break;
					}
				}
			}
			finally
			{
				// never leave modifiers stuck
				Send(Key(VK_SHIFT, true), Key(VK_CONTROL, true), Key(VK_MENU, true));
				foreach(Tok t in toks) if(t.Text != null) t.Text = null;
			}
		}
	}
}
