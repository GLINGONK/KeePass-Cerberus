using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Reflection;
using System.Text;

namespace Cerberus.App.Services
{
	/// <summary>
	/// UI translations. Source strings (French) are the keys; Lang.tsv holds
	/// one column per language: fr, en, de, es, it, pt, nl, pl.
	/// Unknown strings are returned unchanged, so T() is idempotent.
	/// </summary>
	internal static class L
	{
		public static readonly string[] Codes = { "fr", "en", "de", "es", "it", "pt", "nl", "pl" };
		public static readonly string[] Names = { "Français", "English", "Deutsch", "Español", "Italiano", "Português", "Nederlands", "Polski" };

		private static Dictionary<string, string[]> g_d;
		private static int g_col = 1; // English by default
		private const int ColEn = 1;

		public static string Current { get { return Codes[g_col]; } }

		/// <summary>"auto" = Windows display language when supported, else English.</summary>
		public static void Init(string strSetting)
		{
			Load();
			string code = strSetting;
			if(string.IsNullOrEmpty(code) || code == "auto")
				code = CultureInfo.CurrentUICulture.TwoLetterISOLanguageName.ToLowerInvariant();
			int i = Array.IndexOf(Codes, code);
			g_col = (i >= 0) ? i : ColEn;
		}

		private static void Load()
		{
			g_d = new Dictionary<string, string[]>(StringComparer.Ordinal);
			try
			{
				Assembly a = Assembly.GetExecutingAssembly();
				using(Stream s = a.GetManifestResourceStream("Cerberus.App.Lang.tsv"))
				using(StreamReader r = new StreamReader(s, Encoding.UTF8))
				{
					string line;
					while((line = r.ReadLine()) != null)
					{
						if(line.Length == 0 || line.StartsWith("#")) continue;
						string[] v = line.Split('\t');
						if(v.Length < Codes.Length) continue;
						for(int k = 0; k < v.Length; ++k)
						{
							string t = v[k].Replace("\\n", "\n");
							// keep the key's leading/trailing spaces (fragments glued with values)
							if(k > 0 && t.Length > 0)
							{
								t = t.Trim(' ');
								if(v[0].StartsWith(" ")) t = " " + t;
								if(v[0].EndsWith(" ")) t = t + " ";
							}
							v[k] = t;
						}
						g_d[v[0]] = v;
					}
				}
			}
			catch(Exception) { }
		}

		public static string T(string fr)
		{
			if(fr == null || g_d == null || g_col == 0) return fr;
			string[] v;
			if(!g_d.TryGetValue(fr, out v)) return fr;
			string t = v[g_col];
			if(string.IsNullOrEmpty(t)) t = v[ColEn];
			return string.IsNullOrEmpty(t) ? fr : t;
		}
	}
}
