using System;
using System.Collections.Generic;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;

using KeePassLib;
using KeePassLib.Cryptography;
using KeePassLib.Security;

namespace Cerberus.App.Services
{
	/// <summary>RFC 6238 TOTP. Reads KeePass (TimeOtp-*) and KeePassXC ("otp" URI) fields.</summary>
	internal static class Totp
	{
		public sealed class Params
		{
			public byte[] Secret;
			public int Digits = 6;
			public int Period = 30;
			public string Algorithm = "SHA1";
		}

		public static Params FromEntry(PwEntry pe)
		{
			try
			{
				string strUri = pe.Strings.ReadSafe("otp");
				if(strUri.StartsWith("otpauth://", StringComparison.OrdinalIgnoreCase)) return FromUri(strUri);

				string b32 = pe.Strings.ReadSafe("TimeOtp-Secret-Base32");
				string utf8 = pe.Strings.ReadSafe("TimeOtp-Secret");
				string hex = pe.Strings.ReadSafe("TimeOtp-Secret-Hex");
				Params p = new Params();
				if(b32.Length > 0) p.Secret = Base32(b32);
				else if(hex.Length > 0) p.Secret = KeePassLib.Utility.MemUtil.HexStringToByteArray(hex);
				else if(utf8.Length > 0) p.Secret = Encoding.UTF8.GetBytes(utf8);
				else return null;
				int n;
				if(int.TryParse(pe.Strings.ReadSafe("TimeOtp-Length"), out n) && n >= 6 && n <= 8) p.Digits = n;
				if(int.TryParse(pe.Strings.ReadSafe("TimeOtp-Period"), out n) && n > 0) p.Period = n;
				string a = pe.Strings.ReadSafe("TimeOtp-Algorithm");
				if(a.Length > 0) p.Algorithm = a.Replace("HMAC-", "").ToUpperInvariant();
				return p;
			}
			catch(Exception) { return null; }
		}

		public static Params FromUri(string strUri)
		{
			Uri u = new Uri(strUri);
			Params p = new Params();
			foreach(string kv in u.Query.TrimStart('?').Split('&'))
			{
				int i = kv.IndexOf('=');
				if(i <= 0) continue;
				string k = kv.Substring(0, i).ToLowerInvariant();
				string v = Uri.UnescapeDataString(kv.Substring(i + 1));
				if(k == "secret") p.Secret = Base32(v);
				else if(k == "digits") p.Digits = int.Parse(v);
				else if(k == "period") p.Period = int.Parse(v);
				else if(k == "algorithm") p.Algorithm = v.ToUpperInvariant();
			}
			return (p.Secret != null) ? p : null;
		}

		public static byte[] Base32(string s)
		{
			s = s.Trim().Replace(" ", "").Replace("-", "").TrimEnd('=').ToUpperInvariant();
			const string A = "ABCDEFGHIJKLMNOPQRSTUVWXYZ234567";
			List<byte> l = new List<byte>();
			int buf = 0, bits = 0;
			foreach(char c in s)
			{
				int v = A.IndexOf(c);
				if(v < 0) throw new FormatException("Base32");
				buf = (buf << 5) | v; bits += 5;
				if(bits >= 8) { l.Add((byte)(buf >> (bits - 8))); bits -= 8; }
			}
			return l.ToArray();
		}

		public static string Code(Params p, DateTime utcNow, out int nSecondsLeft)
		{
			long t = (long)(utcNow - new DateTime(1970, 1, 1, 0, 0, 0, DateTimeKind.Utc)).TotalSeconds;
			long c = t / p.Period;
			nSecondsLeft = (int)(p.Period - (t % p.Period));
			byte[] msg = BitConverter.GetBytes(c);
			if(BitConverter.IsLittleEndian) Array.Reverse(msg);
			HMAC h;
			if(p.Algorithm == "SHA256") h = new HMACSHA256(p.Secret);
			else if(p.Algorithm == "SHA512") h = new HMACSHA512(p.Secret);
			else h = new HMACSHA1(p.Secret);
			using(h)
			{
				byte[] mac = h.ComputeHash(msg);
				int o = mac[mac.Length - 1] & 0x0F;
				int bin = ((mac[o] & 0x7F) << 24) | (mac[o + 1] << 16) | (mac[o + 2] << 8) | mac[o + 3];
				int mod = (int)Math.Pow(10, p.Digits);
				return (bin % mod).ToString().PadLeft(p.Digits, '0');
			}
		}
	}

	/// <summary>Password generator: uniform sampling with CryptoRandom (rejection sampling).</summary>
	internal static class PasswordGen
	{
		private const string Upper = "ABCDEFGHIJKLMNOPQRSTUVWXYZ";
		private const string Lower = "abcdefghijklmnopqrstuvwxyz";
		private const string Digits = "0123456789";
		private const string Symbols = "!#$%&()*+,-./:;<=>?@[]^_{|}~";
		private const string Ambiguous = "Il1O0o|`'\"";

		public static string Alphabet(bool u, bool l, bool d, bool s, bool bNoAmb)
		{
			StringBuilder sb = new StringBuilder();
			if(u) sb.Append(Upper);
			if(l) sb.Append(Lower);
			if(d) sb.Append(Digits);
			if(s) sb.Append(Symbols);
			string a = sb.ToString();
			if(bNoAmb) a = new string(a.Where(c => Ambiguous.IndexOf(c) < 0).ToArray());
			return a;
		}

		private static int Uniform(int n)
		{
			// rejection sampling over 32-bit values
			uint lim = uint.MaxValue - (uint.MaxValue % (uint)n);
			while(true)
			{
				byte[] pb = CryptoRandom.Instance.GetRandomBytes(4);
				uint v = BitConverter.ToUInt32(pb, 0);
				if(v < lim) return (int)(v % (uint)n);
			}
		}

		public static char[] Generate(int nLength, bool u, bool l, bool d, bool s, bool bNoAmb)
		{
			string a = Alphabet(u, l, d, s, bNoAmb);
			if(a.Length == 0 || nLength <= 0) return new char[0];
			List<string> sets = new List<string>();
			if(u) sets.Add(Upper); if(l) sets.Add(Lower); if(d) sets.Add(Digits); if(s) sets.Add(Symbols);
			for(int attempt = 0; attempt < 100; ++attempt)
			{
				char[] v = new char[nLength];
				for(int i = 0; i < nLength; ++i) v[i] = a[Uniform(a.Length)];
				// require one char of every chosen class when length allows
				bool ok = nLength < sets.Count || sets.All(set => v.Any(c => set.IndexOf(c) >= 0));
				if(ok) return v;
				Array.Clear(v, 0, v.Length);
			}
			char[] w = new char[nLength];
			for(int i = 0; i < nLength; ++i) w[i] = a[Uniform(a.Length)];
			return w;
		}

		public static double Entropy(int nLength, int nAlphabet)
		{
			return (nAlphabet <= 1) ? 0 : nLength * Math.Log(nAlphabet, 2);
		}

		public static uint Estimate(char[] v)
		{
			return (v == null || v.Length == 0) ? 0 : QualityEstimation.EstimatePasswordBits(v);
		}

		public static string Label(uint bits)
		{
			if(bits == 0) return L.T("vide");
			if(bits < 50) return L.T("faible");
			if(bits < 80) return L.T("correct");
			if(bits < 110) return L.T("fort");
			return L.T("très fort");
		}
	}

	/// <summary>Categories = plain KeePass tags (compatible with every KDBX client).</summary>
	internal static class Categories
	{
		public sealed class Cat
		{
			public string Name { get; set; }
			public string DisplayName { get { return L.T(Name); } }
			public string Icon { get; set; }   // geometry resource key
			public string Color { get; set; }
			public int Count { get; set; }
			public bool IsAll, IsNone, IsFolder, IsBin;
			public PwGroup Group;
			public int Depth { get; set; }
		}

		public static readonly string[] Names = {
			"Connexions", "Banque", "Cartes", "Identités", "Notes", "Wi-Fi", "Serveurs", "Licences" };

		public static readonly Dictionary<string, string> Icons = new Dictionary<string, string> {
			{ "Connexions", "IcGlobe" }, { "Banque", "IcBank" }, { "Cartes", "IcCard" }, { "Identités", "IcId" },
			{ "Notes", "IcNote" }, { "Wi-Fi", "IcWifi" }, { "Serveurs", "IcServer" }, { "Licences", "IcLicense" } };

		public static readonly Dictionary<string, string> Colors = new Dictionary<string, string> {
			{ "Connexions", "#50AAFF" }, { "Banque", "#2EC48C" }, { "Cartes", "#FFAA3C" }, { "Identités", "#BE78FF" },
			{ "Notes", "#FFD250" }, { "Wi-Fi", "#3CC8DC" }, { "Serveurs", "#FF6E6E" }, { "Licences", "#A0C85A" } };

		public static string Of(PwEntry pe)
		{
			foreach(string t in pe.Tags)
				foreach(string n in Names)
					if(string.Equals(t, n, StringComparison.OrdinalIgnoreCase)) return n;
			return null;
		}

		public static bool Set(PwEntry pe, string strCat)
		{
			string cur = Of(pe);
			if(string.Equals(cur, strCat, StringComparison.OrdinalIgnoreCase)) return false;
			foreach(string n in Names) pe.RemoveTag(n);
			if(!string.IsNullOrEmpty(strCat)) pe.AddTag(strCat);
			return true;
		}

		public static string Guess(PwEntry pe)
		{
			string title = pe.Strings.ReadSafe(PwDefs.TitleField), url = pe.Strings.ReadSafe(PwDefs.UrlField);
			string user = pe.Strings.ReadSafe(PwDefs.UserNameField), pw = pe.Strings.ReadSafe(PwDefs.PasswordField);
			string notes = pe.Strings.ReadSafe(PwDefs.NotesField);
			string grp = pe.ParentGroup != null ? pe.ParentGroup.Name : "";
			string all = title + " " + url + " " + grp + " " + notes;
			foreach(var kv in pe.Strings) if(!PwDefs.IsStandardField(kv.Key)) all += " " + kv.Key;
			RegexOptions o = RegexOptions.IgnoreCase;
			if(Regex.IsMatch(all, @"wi-?fi|ssid|wpa|wlan|livebox|freebox|routeur", o)) return "Wi-Fi";
			if(Regex.IsMatch(title + " " + grp, @"carte|card|visa|mastercard|amex|cvv|cvc", o) ||
				Regex.IsMatch(user + " " + notes, @"(?:\d[ -]?){13,19}")) return "Cartes";
			if(Regex.IsMatch(all, @"banque|bank|crédit|credit agricole|caisse d'épargne|bnp|société générale|lcl|boursorama|paypal|revolut|n26|iban|assurance", o)) return "Banque";
			if(Regex.IsMatch(all, @"passeport|passport|identité|identity|sécu|permis|impots|impôts|ameli|caf\b|france ?connect", o)) return "Identités";
			if(Regex.IsMatch(all, @"licen[cs]e|serial|clé produit|product key|activation", o)) return "Licences";
			if(Regex.IsMatch(all, @"ssh|ftp|sftp|rdp|vpn|serveur|server|nas|cpanel|mysql|postgres|database|^\d{1,3}(\.\d{1,3}){3}", o)) return "Serveurs";
			if(pw.Length == 0 && user.Length == 0 && notes.Length > 0) return "Notes";
			if(url.Length > 0 || user.Length > 0) return "Connexions";
			return null;
		}
	}
}
